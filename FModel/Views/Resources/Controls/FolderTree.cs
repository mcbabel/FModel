using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FModel.Extensions;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Flat ListBox showing <see cref="AssetsFolderViewModel.VisibleFolders"/> as a tree (indentation via TreeItem.Indent).
/// The expander arrow of each row is a ToggleButton with the "FolderExpanderStyle" class.
/// </summary>
public sealed class FolderTree : ListBox
{
    public static readonly StyledProperty<AssetsFolderViewModel> FoldersProperty =
        AvaloniaProperty.Register<FolderTree, AssetsFolderViewModel>(nameof(Folders));

    public static readonly RoutedEvent<RoutedEventArgs> OpenAssetsEvent =
        RoutedEvent.Register<FolderTree, RoutedEventArgs>(nameof(OpenAssets), RoutingStrategies.Bubble);

    protected override Type StyleKeyOverride => typeof(ListBox);

    public AssetsFolderViewModel Folders
    {
        get => GetValue(FoldersProperty);
        set => SetValue(FoldersProperty, value);
    }

    public event EventHandler<RoutedEventArgs> OpenAssets
    {
        add => AddHandler(OpenAssetsEvent, value);
        remove => RemoveHandler(OpenAssetsEvent, value);
    }

    public FolderTree()
    {
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(DoubleTappedEvent, OnRowDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void RaiseOpenAssets() => RaiseEvent(new RoutedEventArgs(OpenAssetsEvent, this));

    public void SelectFolder(TreeItem folder)
    {
        Folders.Reveal(folder);
        SetCurrentValue(SelectedItemProperty, folder);
        this.RevealItem(folder);
    }

    public void FocusSelection()
    {
        if (SelectedItem is TreeItem folder)
        {
            if (this.RevealItem(folder) is { } container)
                container.Focus();
            else
                Focus();
        }
        else
        {
            Focus();
        }
    }

    private void ToggleFolder(TreeItem folder)
    {
        if (Folders == null)
            return;

        // only animate the arrow when the user toggles a folder (not when a recycled row gets another folder)
        var arrow = (ContainerFromItem(folder) as ListBoxItem)?.FindVisualChild<ToggleButton>();
        if (arrow != null)
        {
            arrow.Classes.Add("animate");
            DispatcherTimer.RunOnce(() => arrow.Classes.Remove("animate"), TimeSpan.FromMilliseconds(250));
        }

        Folders.Toggle(folder);
    }

    private static Control ContainerOf(object source) => (source as StyledElement)?.FindAncestor<ListBoxItem>();

    private void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is StyledElement source && source.FindAncestor<ToggleButton>() is { DataContext: TreeItem folder } && ContainerOf(source) != null)
        {
            e.Handled = true;
            ToggleFolder(folder);
        }
    }

    private void OnRowDoubleTapped(object sender, TappedEventArgs e)
    {
        if (e.Source is not StyledElement source || source.FindAncestor<ToggleButton>() != null || ContainerOf(source) is not { DataContext: TreeItem folder })
            return;

        e.Handled = true;
        if (folder.Folders.Count > 0)
        {
            ToggleFolder(folder);
        }
        else if (folder.AssetsList.Assets.Count > 0)
        {
            RaiseOpenAssets();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Folders == null || SelectedItem is not TreeItem folder || ItemCount == 0)
            return;

        switch (e.Key)
        {
            case Key.Up:
                SelectFolder((TreeItem) Items[Math.Max(0, SelectedIndex - 1)]);
                break;
            case Key.Down:
                SelectFolder((TreeItem) Items[Math.Min(ItemCount - 1, SelectedIndex + 1)]);
                break;
            case Key.Home:
                SelectFolder((TreeItem) Items[0]);
                break;
            case Key.End:
                SelectFolder((TreeItem) Items[ItemCount - 1]);
                break;
            case Key.Left:
                if (folder.IsExpanded)
                {
                    ToggleFolder(folder);
                }
                else if (folder.Parent != null)
                {
                    SelectFolder(folder.Parent);
                }
                break;
            case Key.Right:
                if (!folder.IsExpanded)
                {
                    ToggleFolder(folder);
                }
                else if (folder.Folders.Count > 0)
                {
                    SelectFolder(folder.Folders[0]);
                }
                break;
            case Key.Enter:
                if ((folder.IsExpanded || folder.Folders.Count == 0) && folder.AssetsList.Assets.Count > 0)
                {
                    e.Handled = true;
                    RaiseOpenAssets();
                    return;
                }

                while (folder.Folders.Count == 1 && folder.AssetsList.Assets.Count == 0)
                    folder = folder.Folders[0];

                SelectFolder(folder);
                if (!folder.IsExpanded)
                {
                    ToggleFolder(folder);
                }
                break;
            default:
                return;
        }

        e.Handled = true;
        FocusSelection();
    }
}
