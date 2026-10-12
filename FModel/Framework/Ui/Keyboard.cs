using Avalonia.Threading;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FModel.Framework.Ui;

/// <summary>Same names and values as System.Windows.Input.ModifierKeys (keeps saved hotkeys compatible).</summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>Minimal replacement of WPF's static Keyboard class.</summary>
public static class Keyboard
{
    public static ModifierKeys Modifiers { get; private set; }

    public static IInputElement FocusedElement
    {
        get
        {
            var top = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? FindActive(desktop) : null;
            return top?.FocusManager?.GetFocusedElement();
        }
    }

    private static TopLevel FindActive(Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
    {
        foreach (var window in desktop.Windows)
            if (window.IsActive) return window;
        return desktop.MainWindow;
    }

    public static ModifierKeys FromKeyModifiers(KeyModifiers modifiers) => (ModifierKeys) (int) modifiers;
    public static KeyModifiers ToKeyModifiers(ModifierKeys modifiers) => (KeyModifiers) (int) modifiers;

    internal static void Update(KeyModifiers modifiers) => Modifiers = FromKeyModifiers(modifiers);

    /// <summary>Registers global handlers that keep <see cref="Modifiers"/> up to date.</summary>
    public static void Initialize()
    {
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, e) => Update(e.KeyModifiers), RoutingStrategies.Tunnel, true);
        InputElement.KeyUpEvent.AddClassHandler<TopLevel>((_, e) => Update(e.KeyModifiers), RoutingStrategies.Tunnel, true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => Update(e.KeyModifiers), RoutingStrategies.Tunnel, true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) => Update(e.KeyModifiers), RoutingStrategies.Tunnel, true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, e) => Update(e.KeyModifiers), RoutingStrategies.Tunnel, true);
    }
}
