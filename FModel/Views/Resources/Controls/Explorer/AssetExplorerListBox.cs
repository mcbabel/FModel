using System;
using Avalonia;
using Avalonia.Controls;
using FModel.Extensions;

namespace FModel.Views.Resources.Controls.Explorer;

/// <summary>ListBox used by the assets explorer (tiled and list views). Lazily resolves the visible GameFileViewModels
/// (see <see cref="AssetExplorerBehavior"/>), opens files/folders on double click and shows FolderContextMenu on folders.</summary>
public class AssetExplorerListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    public AssetExplorerListBox()
    {
        AssetExplorerBehavior.SetResolveVisibleItems(this, true);
        AssetExplorerBehavior.SetIsExplorer(this, true);
    }

    /// <summary>Calls GameFileViewModel.OnIsVisible() for every item currently shown in the viewport.</summary>
    public void RefreshVisibleItems() => AssetExplorerBehavior.RefreshVisibleItems(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Always start at the top, otherwise inherited offset can cause items not to be visible when we change folder
        if (change.Property == ItemsSourceProperty && this.FindVisualChild<ScrollViewer>() is { } scroll)
        {
            scroll.Offset = new Vector(scroll.Offset.X, 0);
        }
    }
}
