using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Editing;
using FModel.Extensions;
using FModel.Framework.Ui;
using FModel.Services;
using FModel.Settings;
using FModel.ViewModels;
using FModel.Views;
using FModel.Views.Resources.Controls;

namespace FModel;

public partial class MainWindow : FWindow
{
    public static MainWindow Instance => (MainWindow) AppWindows.Main;
    private ThreadWorkerViewModel _threadWorkerView => ApplicationService.ThreadWorkerView;
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;
    private DiscordHandler _discordHandler => DiscordService.DiscordHandler;

    /// <summary>Status bar text: "{Label} …" while loading, "… ESC to Cancel" when the operation can be canceled.</summary>
    public static readonly IMultiValueConverter StatusTextConverter = new FuncMultiValueConverter<object, string>(values =>
    {
        var list = values.ToList();
        var label = list.ElementAtOrDefault(0) as string ?? string.Empty;
        var kind = list.ElementAtOrDefault(1) is EStatusKind k ? k : EStatusKind.Ready;
        var canBeCanceled = list.ElementAtOrDefault(2) is true;

        if (kind != EStatusKind.Loading) return label;
        return canBeCanceled ? $"{label} …    ESC to Cancel" : $"{label} …";
    });

    public MainWindow()
    {
        var area = ScreenInfo.PrimaryWorkingAreaDipsOf(this);
        Width = area.Width * 0.90;
        Height = area.Height * 0.95;

        DataContext = _applicationView;
        InitializeComponent();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AssetsListName.AddHandler(DoubleTappedEvent, OnAssetsListMouseDoubleClick, RoutingStrategies.Bubble, true);
        AssetsListName.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AssetsExplorer.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AssetsListExplorer.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        AssetsExplorer.SelectionChanged += (_, e) => SyncSelection(AssetsListName, e);
        AssetsListExplorer.SelectionChanged += (_, e) => SyncSelection(AssetsListName, e);
        AssetsListName.SelectionChanged += (_, e) => SyncSelection(AssetsExplorer, e);

        _threadWorkerView.PropertyChanged += OnThreadWorkerPropertyChanged;

        Closing += OnClosing;
        Loaded += OnLoaded;

        FLogger.Logger = LogRtbName;
    }

    // Hack to sync selection between packages tab and explorer
    private void SyncSelection(ListBox target, SelectionChangedEventArgs e)
    {
        if (target.SelectedItems is not { } selectedItems)
            return;

        foreach (var added in e.AddedItems.OfType<GameFileViewModel>())
        {
            if (!selectedItems.Contains(added))
                selectedItems.Add(added);
        }

        foreach (var removed in e.RemovedItems.OfType<GameFileViewModel>())
        {
            if (selectedItems.Contains(removed))
                selectedItems.Remove(removed);
        }

        // keep SelectedItem (used by the information panel) in sync as well
        if (target.SelectedItem == null && selectedItems.Count > 0)
            target.SelectedItem = selectedItems[0];
    }

    private void OnClosing(object sender, WindowClosingEventArgs e)
    {
        _discordHandler.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var newOrUpdated = UserSettings.Default.ShowChangelog;
#if !DEBUG
        ApplicationService.ApiEndpointView.FModelApi.CheckForUpdates(true);
#endif

        switch (UserSettings.Default.AesReload)
        {
            case EAesReload.Always:
                await _applicationView.CUE4Parse.RefreshAes();
                break;
            case EAesReload.OncePerDay when UserSettings.Default.CurrentDir.LastAesReload != DateTime.Today:
                UserSettings.Default.CurrentDir.LastAesReload = DateTime.Today;
                await _applicationView.CUE4Parse.RefreshAes();
                break;
        }

        await Task.WhenAll(
            ApplicationViewModel.InitOodle(),
            ApplicationViewModel.InitZlib()
        );

        await _applicationView.CUE4Parse.Initialize();
        await _applicationView.AesManager.InitAes();
        await _applicationView.UpdateProvider(true);
#if !DEBUG
        await _applicationView.CUE4Parse.InitInformation();
#endif

        await Task.WhenAll(
            _applicationView.CUE4Parse.VerifyConsoleVariables(),
            _applicationView.CUE4Parse.VerifyOnDemandArchives(),
            _applicationView.CUE4Parse.InitMappings(),
            ApplicationViewModel.InitDetex(),
            ApplicationViewModel.InitVgmStream(),
            ApplicationViewModel.InitImGuiSettings(newOrUpdated),
            Task.Run(() =>
            {
                if (UserSettings.Default.DiscordRpc == EDiscordRpc.Always)
                    _discordHandler.Initialize(_applicationView.GameDisplayName);
            }),
            UserSettings.Default.DecompileLua ? ApplicationViewModel.InitUnluac() : Task.CompletedTask
        ).ConfigureAwait(false);
    }

    private void OnThreadWorkerPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // these flags are raised and immediately lowered, read them synchronously
        switch (e.PropertyName)
        {
            case nameof(ThreadWorkerViewModel.StatusChangeAttempted) when _threadWorkerView.StatusChangeAttempted:
                Dispatcher.UIThread.Post(() => FlashStatusBar(["#CE5555", "#C22B2B", "#CE5555", "#C22B2B", "#CE5555"], 200));
                break;
            case nameof(ThreadWorkerViewModel.OperationCancelled) when _threadWorkerView.OperationCancelled:
                Dispatcher.UIThread.Post(() => FlashStatusBar(["#C22B2B", "#CE5555"], 1000));
                break;
        }
    }

    private int _flashId;
    private async void FlashStatusBar(string[] colors, int stepMs)
    {
        var id = ++_flashId;
        foreach (var color in colors)
        {
            if (id != _flashId) return;
            MainStatusBar.Background = new SolidColorBrush(Color.Parse(color));
            await Task.Delay(stepMs);
        }

        if (id == _flashId)
            MainStatusBar.ClearValue(BackgroundProperty);
    }

    private void OnGridSplitterDoubleClick(object sender, TappedEventArgs e)
    {
        RootGrid.ColumnDefinitions[0].Width = GridLength.Auto;
    }

    private void OnWindowPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // mouse "back" button, WPF NavigationCommands.BrowseBack
        if (e.GetCurrentPoint(this).Properties.IsXButton1Pressed)
        {
            OnBrowseBack();
            e.Handled = true;
        }
    }

    private void OnBrowseBack()
    {
        if (UserSettings.Default.FeaturePreviewNewAssetExplorer && !_applicationView.IsAssetsExplorerVisible)
        {
            // back browsing the json view will reopen the assets explorer
            _applicationView.IsAssetsExplorerVisible = true;
            return;
        }

        if (LeftTabControl.SelectedIndex == 2)
        {
            LeftTabControl.SelectedIndex = 1;
        }
        else if (LeftTabControl.SelectedIndex == 1 && AssetsFolderName.SelectedItem is TreeItem { Parent: TreeItem parent })
        {
            AssetsFolderName.Focus();
            SelectFolder(parent);
        }
    }

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);

        if (e.Source is TextBox || e.Source is Visual v && v.FindAncestorOfType<TextArea>(true) != null && ctrl && e.Key != Key.F)
            return;

        if (_threadWorkerView.CanBeCanceled && e.Key == Key.Escape)
        {
            _applicationView.Status.SetStatus(EStatusKind.Stopping);
            _threadWorkerView.Cancel();
        }
        else if (e.Key == Key.F12)
            await _applicationView.CUE4Parse.InitMappings(true);
        else if (e.Key == Key.Left && alt)
            OnBrowseBack();
        else if (_applicationView.Status.IsReady && e.Key == Key.F && ctrl && shift)
            OnSearchViewClick(null, null);
        else if (_applicationView.Status.IsReady && e.Key == Key.R && ctrl && shift)
            OnRefViewClick(null, null);
        else if (e.Key == Key.F3 || e.Key == Key.F && ctrl && !shift)
            OnOpenAvalonFinder();
        else if (e.Key == Key.Left && !_applicationView.IsAssetsExplorerVisible && _applicationView.CUE4Parse.TabControl.SelectedTab is { HasImage: true })
            _applicationView.CUE4Parse.TabControl.SelectedTab.GoPreviousImage();
        else if (e.Key == Key.Right && !_applicationView.IsAssetsExplorerVisible && _applicationView.CUE4Parse.TabControl.SelectedTab is { HasImage: true })
            _applicationView.CUE4Parse.TabControl.SelectedTab.GoNextImage();
        else if (_applicationView.Status.IsReady && _applicationView.IsAssetsExplorerVisible && alt && GetCategoryIndex(e.Key) is var categoryIndex and >= 0)
            CategoriesSelector.SelectedIndex = categoryIndex;
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportData.IsTriggered(e.Key))
            OnExportHotkey("Save_Data");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportProperties.IsTriggered(e.Key))
            OnExportHotkey("Save_Properties");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportTextures.IsTriggered(e.Key))
            OnExportHotkey("Save_Textures");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportModels.IsTriggered(e.Key))
            OnExportHotkey("Save_Models");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportWorlds.IsTriggered(e.Key))
            OnExportHotkey("Save_Worlds");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportAnimations.IsTriggered(e.Key))
            OnExportHotkey("Save_Animations");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportAudio.IsTriggered(e.Key))
            OnExportHotkey("Save_Audio");
        else if (_applicationView.Status.IsReady && UserSettings.Default.ExportCode.IsTriggered(e.Key))
            OnExportHotkey("Save_Code");
        else if (_applicationView.Status.IsReady && UserSettings.Default.FeaturePreviewNewAssetExplorer && UserSettings.Default.SwitchAssetExplorer.IsTriggered(e.Key))
            _applicationView.IsAssetsExplorerVisible = !_applicationView.IsAssetsExplorerVisible;
        else if (UserSettings.Default.AssetAddTab.IsTriggered(e.Key))
            _applicationView.CUE4Parse.TabControl.AddTab();
        else if (UserSettings.Default.AssetRemoveTab.IsTriggered(e.Key))
            _applicationView.CUE4Parse.TabControl.RemoveTab();
        else if (UserSettings.Default.AssetLeftTab.IsTriggered(e.Key))
            _applicationView.CUE4Parse.TabControl.GoLeftTab();
        else if (UserSettings.Default.AssetRightTab.IsTriggered(e.Key))
            _applicationView.CUE4Parse.TabControl.GoRightTab();
        else if (UserSettings.Default.DirLeftTab.IsTriggered(e.Key) && _applicationView.SelectedLeftTabIndex > 0)
            _applicationView.SelectedLeftTabIndex--;
        else if (UserSettings.Default.DirRightTab.IsTriggered(e.Key) && _applicationView.SelectedLeftTabIndex < LeftTabControl.ItemCount - 1)
            _applicationView.SelectedLeftTabIndex++;
        else
            return;

        e.Handled = true;
    }

    private static int GetCategoryIndex(Key key) => key switch
    {
        Key.D0 or Key.NumPad0 => 0,
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => -1
    };

    private void OnSearchViewClick(object sender, RoutedEventArgs e)
    {
        var searchView = Helper.GetWindow<SearchView>("Search For Packages", () => new SearchView().Show());
        searchView.FocusTab(ESearchViewTab.SearchView);
    }

    private void OnRefViewClick(object sender, RoutedEventArgs e)
    {
        var searchView = Helper.GetWindow<SearchView>("Search For Packages", () => new SearchView().Show());
        searchView.FocusTab(ESearchViewTab.RefView);
    }

    private void OnTabItemChange(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, LeftTabControl))
            return;

        switch (LeftTabControl.SelectedIndex)
        {
            case 0:
                DirectoryFilesListBox.Focus();
                break;
            case 1:
                AssetsFolderName.FocusSelection();
                break;
            case 2:
                AssetsListName.Focus();
                break;
        }
    }

    private void OnOpenAvalonFinder()
    {
        if (_applicationView.IsAssetsExplorerVisible)
        {
            AssetsExplorerSearch.TextBox.Focus();
            AssetsExplorerSearch.TextBox.SelectAll();
        }
        else if (_applicationView.CUE4Parse.TabControl.SelectedTab is { } tab)
        {
            tab.HasSearchOpen = true;
            if (AvalonEditor.YesWeSearch is { } search)
            {
                search.Focus();
                search.SelectAll();
            }
        }
    }

    private void OnFolderOpenAssets(object sender, RoutedEventArgs e) => _applicationView.SelectedLeftTabIndex = 2;

    private void OnPreviewTexturesToggled(object sender, RoutedEventArgs e)
    {
        if (PreviewTexturesToggle.IsChecked == true)
            AssetsExplorer.RefreshVisibleItems();
    }

    private void OnAssetsTreeSelectedItemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: TreeItem } || !ReferenceEquals(e.Source, sender))
            return;

        _applicationView.IsAssetsExplorerVisible = true;
        _applicationView.SelectedLeftTabIndex = 1;
    }

    public void SelectFolder(TreeItem folder)
    {
        _applicationView.SelectedLeftTabIndex = 1;
        AssetsFolderName.SelectFolder(folder);
        AssetsFolderName.FocusSelection();
    }

    public void SelectAsset(GameFileViewModel asset)
    {
        var useExplorer = UserSettings.Default.FeaturePreviewNewAssetExplorer;

        _applicationView.SelectedLeftTabIndex = useExplorer ? 1 : 2;
        if (useExplorer)
        {
            _applicationView.IsAssetsExplorerVisible = true;
        }

        ListBox list = useExplorer ? UserSettings.Default.ExplorerViewMode == EExplorerViewMode.List ? AssetsListExplorer : AssetsExplorer : AssetsListName;
        list.SelectedItems?.Clear();
        list.SelectedItem = asset;
        UpdateLayout();
        var container = list.RevealItem(asset);
        Activate();
        container?.Focus();
    }

    private async void OnAssetsListMouseDoubleClick(object sender, TappedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var selectedItems = listBox.SelectedItems?.OfType<GameFileViewModel>().Select(gvm => gvm.Asset).ToArray() ?? [];
        if (selectedItems.Length == 0) return;

        await _threadWorkerView.Begin(cancellationToken => { _applicationView.CUE4Parse.ExtractSelected(cancellationToken, selectedItems); });
    }

    private void OnClearFilterClick(object sender, RoutedEventArgs e)
    {
        if (AssetsFolderName.SelectedItem is TreeItem folder)
        {
            folder.SearchText = string.Empty;
            folder.SelectedCategory = EAssetCategory.All;
        }
    }

    private void OnMouseDoubleClick(object sender, TappedEventArgs e)
    {
        if (!_applicationView.Status.IsReady || sender is not ListBox listBox) return;
        UserSettings.Default.LoadingMode = ELoadingMode.Multiple;
        _applicationView.LoadingModes.LoadCommand.Execute(listBox.SelectedItems);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_applicationView.Status.IsReady || sender is not ListBox listBox)
            return;
        if (e.Key != Key.Enter)
            return;
        if (listBox.SelectedItem == null)
            return;

        switch (listBox.SelectedItem)
        {
            case GameFileViewModel file:
                e.Handled = true;
                _applicationView.IsAssetsExplorerVisible = false;
                ApplicationService.ApplicationView.SelectedLeftTabIndex = 2;
                await _threadWorkerView.Begin(cancellationToken => _applicationView.CUE4Parse.ExtractSelected(cancellationToken, [file.Asset]));
                break;
            case TreeItem folder:
                e.Handled = true;
                ApplicationService.ApplicationView.SelectedLeftTabIndex = 1;

                var childFolder = folder;
                while (childFolder.Folders.Count == 1 && childFolder.AssetsList.Assets.Count == 0)
                {
                    _applicationView.CUE4Parse.AssetsFolder.Expand(childFolder);
                    childFolder = childFolder.Folders[0];
                }

                _applicationView.CUE4Parse.AssetsFolder.Expand(childFolder);
                SelectFolder(childFolder);
                break;
        }
    }

    private void FeaturePreviewOnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: false })
            _applicationView.IsAssetsExplorerVisible = false;
    }

    private void OnExportHotkey(string trigger)
    {
        if (!_applicationView.Status.IsReady || Keyboard.FocusedElement is not Visual focused)
            return;

        var focusedList = focused.FindAncestorOfType<ListBox>(true);
        IList selection = ReferenceEquals(focusedList, AssetsFolderName)
            ? new[] { AssetsFolderName.SelectedItem }
            : focusedList?.SelectedItems;

        var exportable = selection?.OfType<object>().Where(static item => item is TreeItem or GameFileViewModel).ToArray() ?? [];
        if (exportable.Length == 0)
            return;

        _applicationView.RightClickMenuCommand.Execute(new object[] { trigger, exportable });
    }
}
