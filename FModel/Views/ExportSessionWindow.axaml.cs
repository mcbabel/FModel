using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.ViewModels;
using Serilog.Events;

namespace FModel.Views;

public partial class ExportSessionWindow : FWindow
{
    /// <summary>"Start an export…" placeholder: visible when not running and there are no class groups (WPF MultiDataTrigger).</summary>
    public static readonly IMultiValueConverter PlaceholderVisibilityConverter = new FuncMultiValueConverter<object, bool>(values =>
    {
        var list = values.ToList();
        return list.ElementAtOrDefault(0) is false && list.ElementAtOrDefault(1) is 0;
    });

    /// <summary>Error and Fatal log levels share the same icon.</summary>
    public static readonly IValueConverter IsErrorLevelConverter =
        new FuncValueConverter<object, bool>(level => level is LogEventLevel.Error or LogEventLevel.Fatal);

    public ExportSessionWindow()
    {
        // WPF: Width = 40% of the screen width, Height = 75% of the screen height
        var area = ScreenInfo.PrimaryWorkingAreaDipsOf(this);
        Width = area.Width * 0.40;
        Height = area.Height * 0.75;

        InitializeComponent();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportSessionViewModel { CanExport: true } viewModel })
        {
            Tabs.SelectedIndex = 0;
            await viewModel.ExportAsync();
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportSessionViewModel viewModel })
            viewModel.CancelExport();
    }

    private void OnClearQueueClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportSessionViewModel viewModel })
            viewModel.ClearQueue();
    }

    private void OnRemoveFromQueueClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ObjectGroupViewModel item } && DataContext is ExportSessionViewModel viewModel)
            viewModel.RemoveFromQueue(item);
    }

    private void OnMakeDefaultOptions(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportSessionViewModel viewModel })
            viewModel.Options.SaveAsUserDefaults();
    }

    private void OnResetOptions(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExportSessionViewModel viewModel })
            viewModel.Options.ResetToUserDefaults();
    }

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;

        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Shell.RevealInFileManager(path);
            }
        }
        catch
        {
            //
        }
    }
}
