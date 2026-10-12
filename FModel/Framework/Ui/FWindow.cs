using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private static readonly HashSet<MenuBase> _openMenus = [];

    static FWindow()
    {
        MenuBase.OpenedEvent.AddClassHandler<MenuBase>((menu, _) => _openMenus.Add(menu), handledEventsToo: true);
        MenuBase.ClosedEvent.AddClassHandler<MenuBase>((menu, _) => _openMenus.Remove(menu), handledEventsToo: true);
    }

    /// <summary>
    /// The synchronous dialogs run a nested loop inside the menu item click handler, before the menu closes itself:
    /// close the open menus first like WPF does (otherwise they stay open behind the dialog).
    /// </summary>
    private static void CloseOpenMenus()
    {
        foreach (var menu in _openMenus.ToArray())
        {
            try
            {
                menu.Close();
            }
            catch
            {
                // ignored
            }
        }

        _openMenus.Clear();
    }

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
        CloseOpenMenus();
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
