using System;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views;

public partial class UpdateView : FWindow
{
    public UpdateView()
    {
        DataContext = new UpdateViewModel();
        InitializeComponent();

        // WPF: MinHeight = 40% of the screen height, Width = 35% of the screen width (height was left to the OS default)
        var screen = ScreenInfo.PrimaryWorkingAreaDips;
        MinHeight = Math.Round(screen.Height * 0.40);
        Height = Math.Round(screen.Height * 0.60);
        Width = Math.Round(screen.Width * 0.35);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UpdateViewModel viewModel) return;
        _ = viewModel.LoadAsync();
    }

    private void OnDownloadLatest(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UpdateViewModel viewModel) return;
        viewModel.DownloadLatest();
    }
}
