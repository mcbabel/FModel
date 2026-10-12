using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FModel.Services;

namespace FModel.Views.Resources.Controls;

public partial class Breadcrumb : UserControl
{
    private const string NavigateNext = "M9.31 6.71c-.39.39-.39 1.02 0 1.41L13.19 12l-3.88 3.88c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l4.59-4.59c.39-.39.39-1.02 0-1.41L10.72 6.7c-.38-.38-1.02-.38-1.41.01z";

    private static readonly IBrush HoverBorderBrush = new SolidColorBrush(Color.FromRgb(127, 127, 144));
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromRgb(72, 73, 92));

    public Breadcrumb()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not string pathAtThisPoint) return;
        InMeDaddy.Children.Clear();

        var folders = pathAtThisPoint.Split('/');
        for (var i = 0; i < folders.Length; i++)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 3, 6, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = i + 1,
                IsEnabled = i < folders.Length - 1,
                Child = new TextBlock
                {
                    Text = folders[i],
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            border.PointerEntered += OnMouseEnter;
            border.PointerExited += OnMouseLeave;
            border.PointerReleased += OnMouseClick;

            InMeDaddy.Children.Add(border);
            if (i >= folders.Length - 1) continue;

            InMeDaddy.Children.Add(new Viewbox
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Canvas
                {
                    Width = 24,
                    Height = 24,
                    Children =
                    {
                        new Path
                        {
                            Fill = Brushes.White,
                            Data = Geometry.Parse(NavigateNext),
                            Opacity = 0.6
                        }
                    }
                }
            });
        }
    }

    private static void OnMouseEnter(object sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = HoverBorderBrush;
            border.Background = HoverBackground;
        }
    }

    private static void OnMouseLeave(object sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.Transparent;
            border.Background = Brushes.Transparent;
        }
    }

    private static void OnMouseClick(object sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { DataContext: string pathAtThisPoint, Tag: int index }) return;

        var directory = string.Join('/', pathAtThisPoint.Split('/').Take(index));
        if (pathAtThisPoint.Equals(directory)) return;

        ApplicationService.ApplicationView.CustomDirectories.GoToCommand.Execute(directory);
    }
}
