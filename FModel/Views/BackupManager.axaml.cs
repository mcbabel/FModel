using System;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views;

public partial class BackupManager : FWindow
{
    private readonly BackupManagerViewModel _viewModel;

    public BackupManager() : this(string.Empty) { }

    public BackupManager(string gameName)
    {
        DataContext = _viewModel = new BackupManagerViewModel(gameName);
        InitializeComponent();

        // WPF: Width = 22.5% of the screen width
        Width = Math.Round(ScreenInfo.PrimaryWorkingAreaDips.Width * 0.225);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.Initialize();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.Download();
    }

    private async void OnCreateBackupClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateBackup();
    }
}
