using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls;

public partial class DropOverlay : UserControl
{
    enum DragStatus
    {
        None,
        File,
        Folder,
    }

    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;
    private DragStatus _dragStatus = DragStatus.None;
    private string _path = null;
    private TopLevel _window;
    private bool _leavePending;

    public DropOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetState()
    {
        _dragStatus = DragStatus.None;
        _path = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this);
        if (window is null || ReferenceEquals(window, _window))
            return;

        _window = window;
        // drag events are raised on the element under the pointer and bubble up to the window
        window.AddHandler(DragDrop.DragEnterEvent, OnPreviewDragEnter, RoutingStrategies.Bubble, true);
        window.AddHandler(DragDrop.DragOverEvent, OnPreviewDragOver, RoutingStrategies.Bubble, true);
        window.AddHandler(DragDrop.DragLeaveEvent, OnPreviewDragLeave, RoutingStrategies.Bubble, true);
        window.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is null)
            return;

        _window.RemoveHandler(DragDrop.DragEnterEvent, OnPreviewDragEnter);
        _window.RemoveHandler(DragDrop.DragOverEvent, OnPreviewDragOver);
        _window.RemoveHandler(DragDrop.DragLeaveEvent, OnPreviewDragLeave);
        _window.RemoveHandler(DragDrop.DropEvent, OnDrop);
        _window = null;
    }

    private void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        // entering another element of the same window: not a real leave
        _leavePending = false;
        if (IsVisible && _dragStatus is not DragStatus.None)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        GetValidTarget(_window, e);
        if (_dragStatus is DragStatus.None)
        {
            e.DragEffects = DragDropEffects.None;
        }
        else
        {
            IsVisible = true;
            e.DragEffects = DragDropEffects.Copy;

            if (_dragStatus is DragStatus.Folder)
            {
                TitleText.Text = "Drop folder to add new game";
                DescriptionText.Text = "Folder will be added to the directory selector";
            }
            else if (_dragStatus is DragStatus.File)
            {
                TitleText.Text = "Drop usmap/jmap to import";
                DescriptionText.Text = "Mapping file will be applied immediately";
            }
        }
        e.Handled = true;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = _dragStatus is DragStatus.None ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave is also raised when moving from one element to another (followed by DragEnter): defer it
        _leavePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_leavePending) return;
            _leavePending = false;
            IsVisible = false;
            ResetState();
        }, DispatcherPriority.Background);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _leavePending = false;
        IsVisible = false;
        e.Handled = true;
        var status = _dragStatus;
        var path = _path;
        ResetState();

        switch (status)
        {
            case DragStatus.Folder:
                await Dispatcher.UIThread.InvokeAsync(() => _applicationView.AddGameDirectory(path));
                break;
            case DragStatus.File:
                UserSettings.IsEndpointValid(EEndpointType.Mapping, out var oldMappingsEndpoint);
                try
                {
                    var newMappingsEndpoint = new EndpointSettings() { Overwrite = true, FilePath = path };
                    UserSettings.Default.CurrentDir.Endpoints[(int) EEndpointType.Mapping] = newMappingsEndpoint;
                    await _applicationView.CUE4Parse.InitMappings();
                    _applicationView.SettingsView.MappingEndpoint = newMappingsEndpoint;
                }
                catch (Exception ex)
                {
                    UserSettings.Default.CurrentDir.Endpoints[(int) EEndpointType.Mapping] = oldMappingsEndpoint;
                    FLogger.Append(ELog.Error, () =>
                    {
                        FLogger.Text($"Failed to load mapping file: {ex.Message}", Constants.WHITE, true);
                    });
                }
                break;
            default:
                break;
        }
    }

    private void GetValidTarget(object sender, DragEventArgs e)
    {
        ResetState();
        if (!_applicationView.Status.IsReady || e.Data.GetFiles() is not { } items)
            return;

        var files = items.Select(x => x.TryGetLocalPath()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (files.Length == 0)
            return;

        bool directorySelectorIsVisible = _applicationView.Status.Kind is EStatusKind.Configuring;
        if (!directorySelectorIsVisible && (Helper.IsWindowOpen<DictionaryEditor>() || Helper.IsWindowOpen<EndpointEditor>()))
        {
            _applicationView.Status.SetStatus(EStatusKind.Configuring);
            ResetState();
            return;
        }

        switch (sender)
        {
            case MainWindow or SettingsView when !directorySelectorIsVisible:
                foreach (var path in files)
                {
                    if (Directory.Exists(path))
                    {
                        _path = path;
                        _dragStatus = DragStatus.Folder;
                        return;
                    }
                    else if (File.Exists(path) && path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        _path = path;
                        _dragStatus = DragStatus.File;
                        return;
                    }
                }
                break;
            case DirectorySelector:
                if (files.FirstOrDefault(f => Directory.Exists(f)) is { } folder)
                {
                    _path = folder;
                    _dragStatus = DragStatus.Folder;
                    return;
                }
                break;
            default:
                break;
        }
        ResetState();
    }
}
