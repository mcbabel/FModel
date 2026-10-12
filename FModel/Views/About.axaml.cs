using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views;

public partial class About : FWindow
{
    private readonly AboutViewModel _viewModel;

    public About()
    {
        DataContext = _viewModel = new AboutViewModel();
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.Initialize();
    }
}
