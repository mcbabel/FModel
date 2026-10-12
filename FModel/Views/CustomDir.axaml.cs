using System;
using Avalonia.Interactivity;
using FModel.Framework.Ui;
using FModel.Settings;

namespace FModel.Views;

public partial class CustomDir : FWindow
{
    public CustomDir() : this(new CustomDirectory()) { }

    public CustomDir(CustomDirectory customDir)
    {
        DataContext = customDir;
        InitializeComponent();

        // WPF: MinHeight = 20% of the screen height, Width = 25% of the screen width
        var screen = ScreenInfo.PrimaryWorkingAreaDips;
        MinHeight = Math.Round(screen.Height * 0.20);
        Width = Math.Round(screen.Width * 0.25);

        Opened += (_, _) =>
        {
            Activate();
            WpfSuckMyDick.Focus();
            WpfSuckMyDick.SelectAll();
        };
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        if (IsVisible) Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        if (IsVisible) Close();
    }
}
