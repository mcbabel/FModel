using Avalonia.Controls;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views;

public partial class StreamingLevelFilterWindow : FWindow
{
    public StreamingLevelFilterWindow(StreamingLevelFilterViewModel vm)
    {
        // WPF: Width = 28% of the screen width, Height = 60% of the screen height
        var area = ScreenInfo.PrimaryWorkingAreaDipsOf(this);
        Width = area.Width * 0.28;
        Height = area.Height * 0.60;

        InitializeComponent();
        DataContext = vm;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StreamingLevelFilterViewModel viewModel })
        {
            viewModel.SkipAll();
            Close();
        }
    }
}
