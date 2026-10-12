using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FModel.Extensions;
using FModel.Services;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls.Explorer;

/// <summary>
/// Attached behaviors for the asset lists (replaces the WPF ItemContainerGenerator.StatusChanged / EventSetter hacks):
/// <para>ResolveVisibleItems: calls GameFileViewModel.OnIsVisible() for the items whose container is inside the viewport
/// (on container realization and scroll/extent/viewport changes).</para>
/// <para>IsExplorer: double click opens a file (new tab) or a folder, folder items get the FolderContextMenu.</para>
/// </summary>
public sealed class AssetExplorerBehavior
{
    public static readonly AttachedProperty<bool> ResolveVisibleItemsProperty =
        AvaloniaProperty.RegisterAttached<AssetExplorerBehavior, ListBox, bool>("ResolveVisibleItems");

    public static readonly AttachedProperty<bool> IsExplorerProperty =
        AvaloniaProperty.RegisterAttached<AssetExplorerBehavior, ListBox, bool>("IsExplorer");

    private static readonly AttachedProperty<bool> IsRefreshPendingProperty =
        AvaloniaProperty.RegisterAttached<AssetExplorerBehavior, ListBox, bool>("IsRefreshPending");

    public static bool GetResolveVisibleItems(ListBox o) => o.GetValue(ResolveVisibleItemsProperty);
    public static void SetResolveVisibleItems(ListBox o, bool value) => o.SetValue(ResolveVisibleItemsProperty, value);
    public static bool GetIsExplorer(ListBox o) => o.GetValue(IsExplorerProperty);
    public static void SetIsExplorer(ListBox o, bool value) => o.SetValue(IsExplorerProperty, value);

    static AssetExplorerBehavior()
    {
        ResolveVisibleItemsProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            list.ContainerPrepared -= OnContainerPrepared;
            list.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
            if (e.NewValue is not true) return;

            list.ContainerPrepared += OnContainerPrepared;
            list.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        });

        IsExplorerProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            list.ContainerPrepared -= OnExplorerContainerPrepared;
            list.RemoveHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
            if (e.NewValue is not true) return;

            list.ContainerPrepared += OnExplorerContainerPrepared;
            list.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        });
    }

    public static void RefreshVisibleItems(ListBox list)
    {
        list.SetValue(IsRefreshPendingProperty, false);
        if (!list.IsEffectivelyVisible || list.FindVisualChild<ScrollViewer>() is not { } scroll)
            return;

        var viewport = new Rect(scroll.Bounds.Size);
        foreach (var container in list.GetRealizedContainers())
        {
            if (container is not { IsVisible: true, DataContext: GameFileViewModel file })
                continue;
            if (container.TranslatePoint(default, scroll) is not { } topLeft)
                continue;
            if (viewport.Intersects(new Rect(topLeft, container.Bounds.Size)))
                file.OnIsVisible();
        }
    }

    private static void ScheduleRefresh(ListBox list)
    {
        if (list.GetValue(IsRefreshPendingProperty)) return;
        list.SetValue(IsRefreshPendingProperty, true);
        Dispatcher.UIThread.Post(() => RefreshVisibleItems(list), DispatcherPriority.Background);
    }

    private static void OnContainerPrepared(object sender, ContainerPreparedEventArgs e) => ScheduleRefresh((ListBox) sender);
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e) => ScheduleRefresh((ListBox) sender);

    private static void OnExplorerContainerPrepared(object sender, ContainerPreparedEventArgs e)
    {
        // WPF opened FolderContextMenu manually for folders, the list itself shows FileContextMenu
        e.Container.ContextMenu = e.Container.DataContext is TreeItem && e.Container.TryFindResource("FolderContextMenu", out var menu)
            ? menu as ContextMenu
            : null;
    }

    private static void OnDoubleTapped(object sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.FindAncestor<ListBoxItem>() is not { } item)
            return;

        switch (item.DataContext)
        {
            case GameFileViewModel file:
                ApplicationService.ApplicationView.SelectedLeftTabIndex = 2;
                file.IsSelected = true;
                file.ExtractAsync();
                break;
            case TreeItem folder:
                ApplicationService.ApplicationView.SelectedLeftTabIndex = 1;

                var childFolder = folder;
                while (childFolder.Folders.Count == 1 && childFolder.AssetsList.Assets.Count == 0)
                    childFolder = childFolder.Folders[0];

                MainWindow.Instance.SelectFolder(childFolder);
                ApplicationService.ApplicationView.CUE4Parse.AssetsFolder.Expand(childFolder);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
