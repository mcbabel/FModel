using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FModel.Framework.Ui;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls.ContextMenus;

/// <summary>
/// The FileContextMenu / FolderContextMenu instances are shared by many controls (Avalonia allows one ContextMenu to be attached
/// to several controls; it is re-parented to the control that opens it). When such a menu opens, its DataContext is replaced
/// by a fresh <see cref="SelectionMenuContext"/> holding a snapshot of the selected items of the ListBox that owns the clicked control
/// (WPF: PlacementTarget.SelectedItems / Tag), so every binding (IsEnabled converters...) is re-evaluated on each opening.
/// </summary>
public sealed class ContextMenuBehavior
{
    public static readonly AttachedProperty<bool> ProvidesSelectionProperty =
        AvaloniaProperty.RegisterAttached<ContextMenuBehavior, ContextMenu, bool>("ProvidesSelection");

    public static bool GetProvidesSelection(ContextMenu o) => o.GetValue(ProvidesSelectionProperty);
    public static void SetProvidesSelection(ContextMenu o, bool value) => o.SetValue(ProvidesSelectionProperty, value);

    static ContextMenuBehavior()
    {
        ProvidesSelectionProperty.Changed.AddClassHandler<ContextMenu>((menu, e) =>
        {
            menu.Opened -= OnOpened;
            if (e.NewValue is true)
                menu.Opened += OnOpened;
        });
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || !ReferenceEquals(e.Source, menu))
            return;

        var listBox = menu.GetLogicalAncestors().OfType<ListBox>().FirstOrDefault();
        var selection = listBox?.SelectedItems?.Cast<object>().ToList() ?? [];
        menu.DataContext = new SelectionMenuContext(selection);
    }
}

/// <summary>DataContext of the shared file/folder context menus.</summary>
public sealed class SelectionMenuContext(List<object> selectedItems)
{
    public ApplicationViewModel App => ApplicationService.ApplicationView;
    public List<object> SelectedItems { get; } = selectedItems;
    public object SelectedItem => SelectedItems.FirstOrDefault();

    public ICommand FavoriteDirectoryCommand { get; } = new RelayCommand(_ =>
    {
        if (selectedItems.FirstOrDefault() is not TreeItem folder)
            return;

        ApplicationService.ApplicationView.CustomDirectories.Add(new CustomDirectory(folder.Header, folder.PathAtThisPoint));
        FLogger.Append(ELog.Information, () =>
            FLogger.Text($"Successfully saved '{folder.PathAtThisPoint}' as a new favorite directory", Constants.WHITE, true));
    });

    public ICommand CopyDirectoryPathCommand { get; } = new RelayCommand(_ =>
    {
        if (selectedItems.FirstOrDefault() is TreeItem folder)
            Clipboard.SetText(folder.PathAtThisPoint);
    });

    private sealed class RelayCommand(Action<object> execute) : ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => execute(parameter);
    }
}
