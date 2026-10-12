using FModel.Framework.Ui;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace FModel.Framework.Ui;

// API modeled after AdonisUI.Controls.MessageBox so existing call sites keep working.

public enum MessageBoxButton { OK, OKCancel, YesNoCancel, YesNo }
public enum MessageBoxImage { None, Error, Question, Warning, Information }
public enum MessageBoxResult { None, OK, Cancel, Yes, No, Custom }

public interface IMessageBoxButtonModel
{
    string Label { get; }
    object Id { get; }
    MessageBoxResult CausedResult { get; }
    bool IsDefault { get; }
    bool IsCancel { get; }
}

public sealed class MessageBoxButtonModel : IMessageBoxButtonModel
{
    public string Label { get; init; }
    public object Id { get; init; }
    public MessageBoxResult CausedResult { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCancel { get; init; }
}

public static class MessageBoxButtons
{
    public static IMessageBoxButtonModel Ok(string label = "OK") => new MessageBoxButtonModel { Label = label, CausedResult = MessageBoxResult.OK, IsDefault = true, IsCancel = true };
    public static IMessageBoxButtonModel Cancel(string label = "Cancel") => new MessageBoxButtonModel { Label = label, CausedResult = MessageBoxResult.Cancel, IsCancel = true };
    public static IMessageBoxButtonModel Yes(string label = "Yes") => new MessageBoxButtonModel { Label = label, CausedResult = MessageBoxResult.Yes, IsDefault = true };
    public static IMessageBoxButtonModel No(string label = "No") => new MessageBoxButtonModel { Label = label, CausedResult = MessageBoxResult.No, IsCancel = true };
    public static IMessageBoxButtonModel Custom(string label, object id = null) => new MessageBoxButtonModel { Label = label, Id = id, CausedResult = MessageBoxResult.Custom };

    public static IMessageBoxButtonModel[] OkCancel() => [Ok(), Cancel()];
    public static IMessageBoxButtonModel[] YesNo() => [Yes(), No()];
    public static IMessageBoxButtonModel[] YesNoCancel() => [Yes(), No(), Cancel()];
}

public class MessageBoxModel
{
    public string Text { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public MessageBoxImage Icon { get; set; }
    public IEnumerable<IMessageBoxButtonModel> Buttons { get; set; } = [MessageBoxButtons.Ok()];
    public bool IsSoundEnabled { get; set; }
    public MessageBoxResult Result { get; set; }
    public IMessageBoxButtonModel ButtonPressed { get; set; }
}

public static class MessageBox
{
    public static MessageBoxResult Show(string text, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var model = new MessageBoxModel
        {
            Text = text,
            Caption = caption,
            Icon = icon,
            Buttons = buttons switch
            {
                MessageBoxButton.OKCancel => MessageBoxButtons.OkCancel(),
                MessageBoxButton.YesNo => MessageBoxButtons.YesNo(),
                MessageBoxButton.YesNoCancel => MessageBoxButtons.YesNoCancel(),
                _ => [MessageBoxButtons.Ok()]
            }
        };
        return Show(model);
    }

    public static MessageBoxResult Show(MessageBoxModel model)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Show(model));

        var window = new MessageBoxWindow(model);
        window.ShowDialog();
        if (window.Pressed is { } pressed)
        {
            model.ButtonPressed = pressed;
            model.Result = pressed.CausedResult;
        }
        else
        {
            var cancel = model.Buttons.FirstOrDefault(b => b.IsCancel);
            model.ButtonPressed = cancel;
            model.Result = cancel?.CausedResult ?? MessageBoxResult.None;
        }

        return model.Result;
    }
}

internal sealed class MessageBoxWindow : FWindow
{
    public IMessageBoxButtonModel Pressed { get; private set; }

    public MessageBoxWindow(MessageBoxModel model)
    {
        Title = model.Caption;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 300;
        MaxWidth = 650;

        var root = new DockPanel { Margin = new Thickness(0) };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 8, 16, 16)
        };
        foreach (var button in model.Buttons)
        {
            var b = new Button { Content = button.Label, MinWidth = 70, IsDefault = button.IsDefault, IsCancel = button.IsCancel };
            if (button.IsDefault) b.Classes.Add("AccentButton");
            var captured = button;
            b.Click += (_, _) =>
            {
                Pressed = captured;
                DialogResult = true;
            };
            buttonsPanel.Children.Add(b);
        }

        DockPanel.SetDock(buttonsPanel, Dock.Bottom);
        var buttonsBorder = new Border { Child = buttonsPanel };
        buttonsBorder.Bind(Border.BackgroundProperty, buttonsBorder.GetResourceObservable("Layer1BackgroundBrush"));
        DockPanel.SetDock(buttonsBorder, Dock.Bottom);
        root.Children.Add(buttonsBorder);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), Spacing = 16 };
        if (CreateIcon(model.Icon) is { } icon)
            content.Children.Add(icon);
        content.Children.Add(new SelectableTextBlock
        {
            Text = model.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(content);

        Content = root;
    }

    private static Control CreateIcon(MessageBoxImage image)
    {
        var (color, glyph) = image switch
        {
            MessageBoxImage.Error => (Color.Parse("#C22B2B"), "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z"),
            MessageBoxImage.Warning => (Color.Parse("#D49220"), "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"),
            MessageBoxImage.Question => (Color.Parse("#206BD4"), "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z"),
            MessageBoxImage.Information => (Color.Parse("#206BD4"), "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"),
            _ => (Colors.Transparent, null)
        };
        if (glyph == null) return null;

        return new Viewbox
        {
            Width = 32,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Canvas
            {
                Width = 24,
                Height = 24,
                Children = { new Path { Fill = new SolidColorBrush(color), Data = Geometry.Parse(glyph) } }
            }
        };
    }
}
