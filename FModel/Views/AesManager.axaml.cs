using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.Services;
using FModel.ViewModels;

namespace FModel.Views;

public partial class AesManager : FWindow
{
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;

    public AesManager()
    {
        DataContext = _applicationView;
        InitializeComponent();

        // WPF: MinHeight = 20% of the screen height, Width = 30% of the screen width (window still sizes to its content)
        var screen = ScreenInfo.PrimaryWorkingAreaDips;
        MinHeight = Math.Round(screen.Height * 0.20);
        MinWidth = Math.Round(screen.Width * 0.30);
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnRefreshAes(object sender, RoutedEventArgs e)
    {
        await _applicationView.CUE4Parse.RefreshAes();
        await _applicationView.AesManager.InitAes();
        _applicationView.AesManager.HasChange = true; // yes even if nothing actually changed
    }

    private async void OnClosing(object sender, WindowClosingEventArgs e)
    {
        await _applicationView.UpdateProvider(false);
    }
}
