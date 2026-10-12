using Avalonia.Controls;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls;

public partial class ExportOptionsControl : UserControl
{
    public ExportOptionsControl()
    {
        InitializeComponent();
    }

    private void OnBrowseOutputDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportOptionsViewModel viewModel })
        {
            var folderBrowser = new FolderBrowserDialog { ShowNewFolderButton = false };
            if (folderBrowser.ShowDialog() == true)
                viewModel.OutputDirectory = folderBrowser.SelectedPath;
        }
    }
}
