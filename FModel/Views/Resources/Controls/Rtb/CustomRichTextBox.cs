using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FModel.Framework.Ui;

namespace FModel.Views.Resources.Controls;

public enum ELog
{
    Information,
    Warning,
    Error,
    Debug,
    None
}

/// <summary>
/// this whole thing needs a refactor
/// TODO: source of truth for logs should be Serilog
/// a custom sink to reroute some of these logs to the UI
/// the ui should be an ObservableCollection of things
/// the things displayed in ListBox (virtualized) should use a custom template
/// see how logs work in ExportSessionViewModel
/// </summary>
public static class FLogger
{
    public static CustomRichTextBox Logger;

    private const string _at = "   at ";
    private const char _dot = '.';
    private const char _colon = ':';
    private const string _gray = "#999";

    public static void Append(ELog type, Action job)
    {
        RunOnUiThread(() =>
        {
            switch (type)
            {
                case ELog.Information:
                    Text("[INF] ", Constants.BLUE);
                    break;
                case ELog.Warning:
                    Text("[WRN] ", Constants.YELLOW);
                    break;
                case ELog.Error:
                    Text("[ERR] ", Constants.RED);
                    break;
                case ELog.Debug:
                    Text("[DBG] ", Constants.GREEN);
                    break;
            }

            job();
        });
    }

    public static void Append(Exception e)
    {
        Append(ELog.Error, () =>
        {
            if ((e.InnerException ?? e) is { TargetSite.DeclaringType: not null } exception)
            {
                if (exception.TargetSite.ToString() == "CUE4Parse.FileProvider.GameFile get_Item(System.String)")
                {
                    Text(e.Message, Constants.WHITE, true);
                }
                else
                {
                    var t = exception.GetType();
                    Text(t.Namespace + _dot, Constants.GRAY);
                    Text(t.Name, Constants.WHITE);
                    Text(_colon + " ", Constants.GRAY);
                    Text(exception.Message, Constants.RED, true);

                    Text(_at, _gray);
                    Text(exception.TargetSite.DeclaringType.FullName + _dot, Constants.GRAY);
                    Text(exception.TargetSite.Name, Constants.YELLOW);

                    var p = exception.TargetSite.GetParameters();
                    var parameters = new string[p.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        parameters[i] = p[i].ParameterType.Name + " " + p[i].Name;
                    }

                    Text("(" + string.Join(", ", parameters) + ")", Constants.GRAY, true);
                }
            }
            else
            {
                Text(e.Message, Constants.WHITE, true);
            }
        });
    }

    public static void Text(string message, string color, bool newLine = false)
    {
        RunOnUiThread(() => Logger?.AppendText(message, color, newLine));
    }

    public static void Link(string message, string url, bool newLine = false)
    {
        RunOnUiThread(() => Logger?.AppendLink(message, url, newLine));
    }

    public static void ClearLogs()
    {
        RunOnUiThread(() => Logger?.Clear());
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}

/// <summary>
/// Read-only, selectable, colored log view (replacement of the WPF RichTextBox/FlowDocument).
/// Style class "CustomRichTextBox" (see ControlStyles.axaml).
/// </summary>
public class CustomRichTextBox : ContentControl
{
    private const int MaxLines = 250; // in the meantime

    private static readonly Dictionary<string, IBrush> _brushes = new();

    private readonly ScrollViewer _scrollViewer;
    private readonly SelectableTextBlock _textBlock;
    private int _lineCount;
    private bool _scrollPending;

    protected override Type StyleKeyOverride => typeof(ContentControl);

    public CustomRichTextBox()
    {
        _textBlock = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            Inlines = new InlineCollection()
        };
        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _textBlock
        };
        Content = _scrollViewer;
    }

    /// <summary>Plain text of the whole log.</summary>
    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var inline in _textBlock.Inlines!)
            {
                switch (inline)
                {
                    case Run run:
                        sb.Append(run.Text);
                        break;
                    case LineBreak:
                        sb.AppendLine();
                        break;
                    case InlineUIContainer { Child: TextBlock link }:
                        sb.Append(link.Text);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    internal void AppendText(string message, string color, bool newLine)
    {
        _textBlock.Inlines!.Add(new Run(message) { Foreground = GetBrush(color) });
        Finally(newLine);
    }

    internal void AppendLink(string message, string url, bool newLine)
    {
        var link = new TextBlock { Text = message, Classes = { "LogLink" } };
        link.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(link).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            OpenLink(url);
        };

        _textBlock.Inlines!.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom });
        Finally(newLine);
    }

    private static void OpenLink(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                Shell.Open(uri.AbsoluteUri);
            else if (Directory.Exists(url))
                Shell.Open(url);
            else
                Shell.RevealInFileManager(url);
        }
        catch
        {
            // ignored
        }
    }

    public void Clear()
    {
        _textBlock.Inlines!.Clear();
        _lineCount = 0;
    }

    public void ScrollToEnd()
    {
        if (_scrollPending) return;
        _scrollPending = true;
        // after the new text has been measured
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            _scrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void Finally(bool newLine)
    {
        var inlines = _textBlock.Inlines!;
        if (newLine)
        {
            inlines.Add(new LineBreak());
            _lineCount++;
        }

        while (_lineCount > MaxLines && inlines.Count > 0)
        {
            var first = inlines[0];
            inlines.RemoveAt(0);
            if (first is LineBreak) _lineCount--;
        }

        ScrollToEnd();
    }

    private static IBrush GetBrush(string color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = Brush.Parse(color).ToImmutable();
            _brushes[color] = brush;
        }
        return brush;
    }
}
