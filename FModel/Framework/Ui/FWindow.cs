using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace FModel.Framework.Ui;

/// <summary>
/// Base window of the application (replacement for AdonisUI's FWindow).
/// Adds WPF-like synchronous <see cref="ShowDialog()"/> and <see cref="DialogResult"/>.
/// </summary>
public class FWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    private static WindowIcon _appIcon;
    private bool _isDialog;
    private bool? _dialogResult;

    public FWindow()
    {
        try
        {
            _appIcon ??= new WindowIcon(AppWindows.OpenResource("Resources/FModel.ico"));
            Icon = _appIcon;
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>Same behavior as WPF: setting it while shown as dialog closes the window.</summary>
    public bool? DialogResult
    {
        get => _dialogResult;
        set
        {
            _dialogResult = value;
            if (_isDialog) Close(value);
        }
    }

    /// <summary>Owner used for the next <see cref="ShowDialog()"/>/<see cref="Show()"/> call (WPF Window.Owner equivalent).</summary>
    public Window OwnerWindow { get; set; }

    /// <summary>Synchronous, modal ShowDialog like WPF (pumps a nested dispatcher loop).</summary>
    public bool? ShowDialog()
    {
        var owner = OwnerWindow ?? AppWindows.Active;
        _isDialog = true;
        try
        {
            if (owner == null || ReferenceEquals(owner, this))
            {
                UiThread.WaitFor(() =>
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource();
                    Closed += (_, _) => tcs.TrySetResult();
                    Show();
                    return tcs.Task;
                });
            }
            else
            {
                UiThread.WaitFor(() => ShowDialog<object>(owner));
            }
        }
        finally
        {
            _isDialog = false;
        }

        return _dialogResult ?? false;
    }

    public new void Show()
    {
        if (OwnerWindow != null) base.Show(OwnerWindow);
        else base.Show();
    }
}
