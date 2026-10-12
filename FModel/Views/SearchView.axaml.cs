using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.VirtualFileSystem;
using FModel.Framework.Ui;
using FModel.Services;
using FModel.ViewModels;

namespace FModel.Views;

public enum ESearchViewTab
{
    SearchView,
    RefView
}

/// <summary>DataContext of <see cref="SearchView"/> (was an anonymous type in WPF).</summary>
public sealed class SearchViewContext
{
    public ApplicationViewModel mainApplication { get; init; }
    public SearchViewModel SearchTab { get; init; }
    public SearchViewModel RefTab { get; init; }
}

public partial class SearchView : FWindow
{
    /// <summary>Sorts the "Archive" column by archive name (WPF used a non-existent "Archive" property).</summary>
    public static readonly IComparer ArchiveComparer = Comparer<object>.Create((a, b) =>
        string.Compare((a as VfsEntry)?.Vfs.Name, (b as VfsEntry)?.Vfs.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>"{count} References for {name}", or "By Reference" while no reference search was done.</summary>
    public static readonly IMultiValueConverter RefTabHeaderConverter = new FuncMultiValueConverter<object, string>(values =>
    {
        var list = values.ToList();
        if (list.ElementAtOrDefault(0) is not int count || list.ElementAtOrDefault(1) is not string name)
            return "By Reference";
        return string.Format(CultureInfo.CurrentCulture, "{0:### ### ##0} References for {1}", count, name);
    });

    private ThreadWorkerViewModel _threadWorkerView => ApplicationService.ThreadWorkerView;
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;
    private SearchViewModel _searchViewModel => _applicationView.CUE4Parse.SearchVm;
    private SearchViewModel _refViewModel => _applicationView.CUE4Parse.RefVm;

    private ESearchViewTab _currentTab = ESearchViewTab.SearchView;

    public SearchView()
    {
        var area = ScreenInfo.PrimaryWorkingAreaDipsOf(this);
        Width = area.Width * 0.65;
        Height = area.Height * 0.75;

        DataContext = new SearchViewContext
        {
            mainApplication = _applicationView,
            SearchTab = _searchViewModel,
            RefTab = _refViewModel,
        };
        InitializeComponent();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        SearchListView.DoubleTapped += OnAssetRowDoubleTapped;
        RefListView.DoubleTapped += OnAssetRowDoubleTapped;

        Activate();
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void FocusTab(ESearchViewTab view)
    {
        _currentTab = view;
        SearchTabControl.SelectedIndex = view switch
        {
            ESearchViewTab.SearchView => 0,
            ESearchViewTab.RefView => 1,
            _ => SearchTabControl.SelectedIndex
        };
        WindowState = WindowState.Normal;
        CurrentTextBox?.Focus();
        CurrentTextBox?.SelectAll();
    }

    public void ChangeCollection(ESearchViewTab view, IEnumerable<GameFile> files, GameFile refFile)
    {
        var vm = view switch
        {
            ESearchViewTab.SearchView => _searchViewModel,
            ESearchViewTab.RefView => _refViewModel,
            _ => null
        };
        vm?.ChangeCollection(files, refFile);
    }

    private async void OnFindRefs(object sender, RoutedEventArgs e)
    {
        if (CurrentListView?.SelectedItem is not GameFile entry)
            return;

        await _threadWorkerView.Begin(_ => _applicationView.CUE4Parse.FindReferences(entry));
    }

    private void OnTabItemChange(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, SearchTabControl) || sender is not TabControl tabControl)
            return;

        _currentTab = tabControl.SelectedIndex switch
        {
            0 => ESearchViewTab.SearchView,
            1 => ESearchViewTab.RefView,
            _ => _currentTab
        };
        CurrentTextBox?.Focus();
        CurrentTextBox?.SelectAll();
    }

    private void OnDeleteSearchClick(object sender, RoutedEventArgs e)
    {
        var viewModel = CurrentViewModel;
        if (viewModel == null)
            return;
        viewModel.FilterText = string.Empty;
        viewModel.RefreshFilter();
    }

    private SearchViewModel CurrentViewModel => _currentTab switch
    {
        ESearchViewTab.SearchView => _applicationView.CUE4Parse.SearchVm,
        ESearchViewTab.RefView => _applicationView.CUE4Parse.RefVm,
        _ => null
    };

    private DataGrid CurrentListView => _currentTab switch
    {
        ESearchViewTab.SearchView => SearchListView,
        ESearchViewTab.RefView => RefListView,
        _ => null
    };

    private TextBox CurrentTextBox => _currentTab switch
    {
        ESearchViewTab.SearchView => SearchTextBox,
        ESearchViewTab.RefView => RefSearchTextBox,
        _ => null
    };

    private async void OnSearchSortClick(object sender, RoutedEventArgs e)
    {
        if (CurrentViewModel is { } viewModel)
            await viewModel.CycleSortSizeMode();
    }

    // WPF: MouseDoubleClick on ListViewItem
    private void OnAssetRowDoubleTapped(object sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(true) == null)
            return;

        OnAssetDoubleClick(sender, e);
    }

    private void OnAssetDoubleClick(object sender, RoutedEventArgs e)
    {
        if (CurrentListView?.SelectedItem is not GameFile entry)
            return;

        NavigateToAssetAndSelect(entry);
    }
    private void OnGoToRefPackage(object sender, RoutedEventArgs e)
    {
        if (_refViewModel.RefFile is not GameFile entry)
            return;

        NavigateToAssetAndSelect(entry);
    }

    private void NavigateToAssetAndSelect(GameFile entry)
    {
        WindowState = WindowState.Minimized;

        var folder = _applicationView.CustomDirectories.GoToCommand.JumpTo(entry.Directory);
        if (folder == null)
            return;

        folder.SearchText = string.Empty;
        folder.SelectedCategory = EAssetCategory.All;

        var mainWindow = MainWindow.Instance;
        mainWindow.Activate();
        mainWindow.SelectFolder(folder);

        var vm = folder.AssetsList.Assets.FirstOrDefault(x => x.Asset == entry);
        if (vm == null)
            return;

        mainWindow.SelectAsset(vm);
    }

    private async void OnAssetExtract(object sender, RoutedEventArgs e)
    {
        if (CurrentListView?.SelectedItem is not GameFile entry)
            return;

        WindowState = WindowState.Minimized;
        await _threadWorkerView.Begin(cancellationToken => _applicationView.CUE4Parse.Extract(cancellationToken, entry, true));

        MainWindow.Instance.Activate();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CurrentViewModel?.RefreshFilter();
    }

    // WPF: StateChanged
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || WindowState != WindowState.Normal || SearchTabControl == null)
            return;

        Activate();
        CurrentTextBox?.Focus();
        CurrentTextBox?.SelectAll();
    }
}
