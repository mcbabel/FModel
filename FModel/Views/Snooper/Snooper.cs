using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Threading;
using FModel.Framework.Ui;
using CUE4Parse.UE4.Assets.Exports;
using FModel.Views.Snooper.Buffers;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace FModel.Views.Snooper;

public class Snooper : GameWindow
{
    public readonly FramebufferObject Framebuffer;
    public readonly Renderer Renderer;

    private readonly SnimGui _gui;

    private bool _init;

    public Snooper(GameWindowSettings gwSettings, NativeWindowSettings nwSettings) : base(gwSettings, nwSettings)
    {
        Framebuffer = new FramebufferObject(ClientSize);
        Renderer = new Renderer(ClientSize.X, ClientSize.Y);

        _gui = new SnimGui(ClientSize.X, ClientSize.Y);
        _init = false;
    }

    public bool TryLoadExport(CancellationToken cancellationToken, UObject dummy, Lazy<UObject> export)
    {
        Renderer.Load(cancellationToken, dummy, export);
        return Renderer.Options.Models.Count > 0;
    }

    public unsafe void WindowShouldClose(bool value, bool clear)
    {
        if (clear)
        {
            Renderer.CameraOp.Speed = 1f;
            Renderer.Save();
        }

        GLFW.SetWindowShouldClose(WindowPtr, value); // start / stop game loop
        IsVisible = !value;
    }

    public unsafe void WindowShouldFreeze(bool value)
    {
        GLFW.SetWindowShouldClose(WindowPtr, value); // start / stop game loop
        IsVisible = true;
    }

    private DispatcherTimer _loopTimer;
    private Stopwatch _loopWatch;
    private TaskCompletionSource _runCompletion;

    /// <summary>
    /// Shows the viewer and runs its frame loop. Unlike GameWindow.Run() this doesn't block the UI thread:
    /// frames are driven by a dispatcher timer so the Avalonia windows stay responsive (on Windows/WPF both
    /// shared the same Win32 message loop, on Linux GLFW and Avalonia have their own event loops).
    /// When called from a worker thread, this method blocks the worker until the viewer is closed, like before.
    /// </summary>
    public override void Run()
    {
        Renderer.Options.SwapMaterial(false);
        Renderer.Options.AnimateMesh(false);

        Task completion = null;
        Dispatcher.UIThread.Invoke(() =>
        {
            WindowShouldClose(false, false);
            completion = StartLoop();
        });

        if (!Dispatcher.UIThread.CheckAccess())
            completion?.Wait();
    }

    private Task StartLoop()
    {
        if (_runCompletion is { Task.IsCompleted: false })
            return _runCompletion.Task; // already running, just shown again

        _runCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Context?.MakeCurrent();
        OnLoad();
        OnResize(new ResizeEventArgs(ClientSize));

        _loopWatch = Stopwatch.StartNew();
        var period = UpdateFrequency <= 0 ? 1.0 / 60.0 : 1.0 / UpdateFrequency;
        _loopTimer = new DispatcherTimer(TimeSpan.FromSeconds(period), DispatcherPriority.Background, (_, _) => Tick());
        _loopTimer.Start();
        return _runCompletion.Task;
    }

    private unsafe void Tick()
    {
        if (GLFW.WindowShouldClose(WindowPtr))
        {
            _loopTimer?.Stop();
            _loopTimer = null;
            OnUnload();
            _runCompletion?.TrySetResult();
            return;
        }

        Context?.MakeCurrent();

        var elapsed = _loopWatch.Elapsed.TotalSeconds;
        _loopWatch.Restart();

        NewInputFrame();
        ProcessWindowEvents(IsEventDriven);
        if (GLFW.WindowShouldClose(WindowPtr))
            return; // closed while processing events, cleaned up on next tick

        OnUpdateFrame(new FrameEventArgs(elapsed));
        OnRenderFrame(new FrameEventArgs(elapsed));
    }

    private unsafe void LoadWindowIcon()
    {
        using var stream = AppWindows.OpenResource("Resources/engine.png");
        using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
        var memoryGroup = img.GetPixelMemoryGroup();
        Memory<byte> array = new byte[memoryGroup.TotalLength * sizeof(Rgba32)];
        var block = MemoryMarshal.Cast<byte, Rgba32>(array.Span);
        foreach (var memory in memoryGroup)
        {
            memory.Span.CopyTo(block);
            block = block[memory.Length..];
        }

        Icon = new WindowIcon(new OpenTK.Windowing.Common.Input.Image(img.Width, img.Height, array.ToArray()));
    }

    protected override void OnLoad()
    {
        if (_init)
        {
            Renderer.Options.SetupModelsAndLights();
            return;
        }

        base.OnLoad();
        CenterWindow();
        LoadWindowIcon();

        GL.ClearColor(OpenTK.Mathematics.Color4.Black);
        GL.Enable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample);
        GL.Enable(EnableCap.VertexProgramPointSize);
        GL.StencilOp(StencilOp.Keep, StencilOp.Replace, StencilOp.Replace);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        Framebuffer.Setup();
        Renderer.Setup();
        _init = true;
    }

    private void ClearWhatHasBeenDrawn()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        if (!IsVisible)
            return;

        ClearWhatHasBeenDrawn(); // clear window background
        Framebuffer.Bind(); // switch to viewport background
        ClearWhatHasBeenDrawn(); // clear viewport background

        Renderer.Render(); // render everything

        Framebuffer.BindMsaa();
        Framebuffer.Bind(0); // switch to window background

        _gui.Render(this); // render UI
        SwapBuffers();
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        if (!IsVisible)
            return;

        var delta = (float) e.Time;

        _gui.Controller.Update(this, delta);
        Renderer.Update(this, delta);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsVisible)
            return;

        _gui.Controller.PressChar((char) e.Unicode);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);

        Framebuffer.WindowResized(e.Width, e.Height);
        Renderer.WindowResized(e.Width, e.Height);

        _gui.Controller.WindowResized(e.Width, e.Height);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        WindowShouldClose(true, true);
    }

    private static bool _glfwConfigured;

    /// <summary>
    /// Must run before GLFW gets initialized.
    /// OpenTK's default error callback throws from native code which kills the process (e.g. on Wayland:
    /// "The platform does not provide the window position"), so errors are logged instead.
    /// On Linux GLFW is forced onto X11 (XWayland on Wayland sessions) like the rest of the UI,
    /// because native Wayland doesn't support window positioning/centering the viewer relies on.
    /// Set FMODEL_GLFW_WAYLAND=1 to use native Wayland anyway.
    /// </summary>
    public static void ConfigureGlfw(GLFWCallbacks.ErrorCallback errorCallback)
    {
        if (_glfwConfigured) return;
        _glfwConfigured = true;

        GLFWProvider.SetErrorCallback(errorCallback);
        if (OperatingSystem.IsLinux() &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
            Environment.GetEnvironmentVariable("FMODEL_GLFW_WAYLAND") != "1")
        {
            try
            {
                GLFW.InitHint(InitHintPlatform.Platform, Platform.X11);
            }
            catch
            {
                // GLFW < 3.4
            }
        }
    }

    public static int GetMaxRefreshFrequency()
    {
        var rf = 60;
        try
        {
            GLFWProvider.EnsureInitialized();
            foreach (var monitor in Monitors.GetMonitors())
                rf = Math.Max(rf, monitor.CurrentVideoMode.RefreshRate);
        }
        catch
        {
            // ignored
        }

        return rf;
    }
}
