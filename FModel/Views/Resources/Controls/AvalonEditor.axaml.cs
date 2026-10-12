using TabItem = FModel.ViewModels.TabItem;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using CUE4Parse.Utils;
using FModel.Extensions;
using FModel.Framework;
using FModel.Services;
using FModel.ViewModels;
using SkiaSharp;

namespace FModel.Views.Resources.Controls;

/// <summary>
/// Logique d'interaction pour AvalonEditor.xaml
/// </summary>
public partial class AvalonEditor : UserControl
{
    /// <summary>Shown by tabs without content: AvaloniaEdit throws on mouse input when the editor has no document.</summary>
    public static TextDocument EmptyDocument { get; } = new();

    public static TextEditor YesWeEditor;
    public static TextBox YesWeSearch;
    private readonly Dictionary<string, NavigationList<int>> _savedCarets = new();
    private NavigationList<int> _caretsOffsets
    {
        get => MyAvalonEditor.Document != null && MyAvalonEditor.Document.FileName != null
            ? _savedCarets.GetOrAdd(MyAvalonEditor.Document.FileName, () => new NavigationList<int>())
            : new NavigationList<int>();
    }
    private bool _ignoreCaret = true;

    public AvalonEditor()
    {
        InitializeComponent();

        YesWeEditor = MyAvalonEditor;
        YesWeSearch = WpfSuckMyDick;
        MyAvalonEditor.TextArea.TextView.LinkTextBackgroundBrush = null;
        MyAvalonEditor.TextArea.TextView.LinkTextForegroundBrush = Brushes.Cornsilk;
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new GamePathElementGenerator());
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new JumpElementGenerator());
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new HexColorElementGenerator());

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        MyAvalonEditor.AddHandler(PointerWheelChangedEvent, OnPreviewMouseWheel, RoutingStrategies.Tunnel);
        MyAvalonEditor.AddHandler(PointerReleasedEvent, OnMouseRelease, RoutingStrategies.Tunnel, true);

        ApplicationService.ApplicationView.CUE4Parse.TabControl.OnTabRemove += OnTabClose;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TabItem dc) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Escape:
                dc.HasSearchOpen = false;
                break;
            case Key.F3: // WPF NavigationCommands.Search
                FindNext(shift);
                e.Handled = true;
                break;
            case Key.Enter when !shift && dc.HasSearchOpen:
                FindNext();
                break;
            case Key.Enter when shift && dc.HasSearchOpen:
                var old = dc.SearchUp;
                dc.SearchUp = true;
                FindNext();
                dc.SearchUp = old;
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.Alt:
                if (_caretsOffsets.Count == 0) return;
                MyAvalonEditor.CaretOffset = _caretsOffsets.MovePrevious;
                MyAvalonEditor.TextArea.Caret.BringCaretToView();
                e.Handled = true;
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.Alt:
                if (_caretsOffsets.Count == 0) return;
                MyAvalonEditor.CaretOffset = _caretsOffsets.MoveNext;
                MyAvalonEditor.TextArea.Caret.BringCaretToView();
                e.Handled = true;
                break;
        }
    }

    private void OnMouseHover(object sender, PointerEventArgs e) => HexColorToolTip.Show(MyAvalonEditor, e);

    private void OnMouseHoverStopped(object sender, PointerEventArgs e) => HexColorToolTip.Hide(MyAvalonEditor);

    private void OnTextChanged(object sender, EventArgs e)
    {
        if (sender is not TextEditor avalonEditor || DataContext is not TabItem tabItem ||
            avalonEditor.Document == null || string.IsNullOrEmpty(avalonEditor.Document.Text))
            return;
        avalonEditor.Document.FileName = tabItem.Entry.PathWithoutExtension;

        if (!_savedCarets.ContainsKey(avalonEditor.Document.FileName))
            _ignoreCaret = true;

        if (!tabItem.ShouldScroll) return;

        var lineNumber = avalonEditor.Document.Text.GetNameLineNumber(tabItem.ScrollTrigger);
        if (lineNumber == -1) lineNumber = 1;

        var line = avalonEditor.Document.GetLineByNumber(lineNumber);
        avalonEditor.Select(line.Offset, line.Length);
        avalonEditor.ScrollToLine(lineNumber);
    }

    private void OnPreviewMouseWheel(object sender, PointerWheelEventArgs e)
    {
        if (DataContext is not TabItem tabItem || e.KeyModifiers != KeyModifiers.Control)
            return;

        var fontSize = tabItem.FontSize + e.Delta.Y * 120 / 50.0; // WPF delta is 120 per notch
        tabItem.FontSize = fontSize switch
        {
            < 6 => 6,
            > 200 => 200,
            _ => fontSize
        };
        e.Handled = true;
    }

    private void OnDeleteSearchClick(object sender, RoutedEventArgs e)
    {
        ((TabItem) DataContext).TextToFind = string.Empty;
    }

    private void FindNext(bool invertLeftRight = false)
    {
        var viewModel = (TabItem) DataContext;
        if (viewModel.Document == null || string.IsNullOrEmpty(viewModel.TextToFind))
            return;

        Regex r;
        if (invertLeftRight)
        {
            viewModel.SearchUp = !viewModel.SearchUp;
            r = GetRegEx();
            viewModel.SearchUp = !viewModel.SearchUp;
        }
        else r = GetRegEx();

        var rightToLeft = r.Options.HasFlag(RegexOptions.RightToLeft);
        var m = r.Match(MyAvalonEditor.Text, rightToLeft ? MyAvalonEditor.SelectionStart : MyAvalonEditor.SelectionStart + MyAvalonEditor.SelectionLength);
        if (m.Success)
        {
            MyAvalonEditor.Select(m.Index, m.Length);
            MyAvalonEditor.TextArea.Caret.BringCaretToView();
        }
        else
        {
            // we have reached the end of the document
            // start again from the beginning/end,
            m = rightToLeft ? r.Match(MyAvalonEditor.Text, MyAvalonEditor.Text.Length - 1) : r.Match(MyAvalonEditor.Text, 0);
            if (!m.Success) return;
            MyAvalonEditor.Select(m.Index, m.Length);
            MyAvalonEditor.TextArea.Caret.BringCaretToView();
        }
    }

    private Regex GetRegEx(bool forceLeftToRight = false)
    {
        Regex r;
        var o = RegexOptions.None;
        var viewModel = (TabItem) DataContext;

        if (viewModel.SearchUp && !forceLeftToRight)
            o |= RegexOptions.RightToLeft;
        if (!viewModel.CaseSensitive)
            o |= RegexOptions.IgnoreCase;

        if (viewModel.UseRegEx)
        {
            r = new Regex(viewModel.TextToFind, o);
        }
        else
        {
            var s = Regex.Escape(viewModel.TextToFind);
            if (viewModel.WholeWord)
                s = "\\W" + s + "\\W";

            r = new Regex(s, o);
        }

        return r;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        ((TabItem) DataContext).HasSearchOpen = false;
    }

    private void OnTabClose(object sender, EventArgs eventArgs)
    {
        if (eventArgs is not TabControlViewModel.TabEventArgs e || e.TabToRemove.Document?.FileName is not { } fileName)
            return;

        if (_savedCarets.ContainsKey(fileName))
            _savedCarets.Remove(fileName);
    }

    private void SaveCaretLoc(int offset)
    {
        if (_ignoreCaret)
        {
            _ignoreCaret = false;
            return;
        } // first always point to the end of the file for some reason

        if (_caretsOffsets.Count >= 10)
            _caretsOffsets.RemoveAt(0);
        if (!_caretsOffsets.Contains(offset))
        {
            _caretsOffsets.Add(offset);
            _caretsOffsets.CurrentIndex = _caretsOffsets.Count - 1;
        }
    }

    private void OnMouseRelease(object sender, PointerReleasedEventArgs e)
    {
        SaveCaretLoc(MyAvalonEditor.CaretOffset);
    }
}

/// <summary>Shows a tooltip filled with the color of the hovered <c>"Hex": "RRGGBBAA"</c> line of a TextEditor.</summary>
internal static class HexColorToolTip
{
    private static readonly Regex _hexColorRegex = new("\"Hex\": \"(?'target'[0-9A-Fa-f]{3,8})\"$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Show(TextEditor editor, PointerEventArgs e)
    {
        var pos = editor.GetPositionFromPoint(e.GetPosition(editor));
        if (pos == null || editor.Document == null) return;

        var line = editor.Document.GetLineByNumber(pos.Value.Line);
        var m = _hexColorRegex.Match(editor.Document.GetText(line.Offset, line.Length));
        if (!m.Success || !m.Groups.TryGetValue("target", out var g)) return;

        var color = SKColor.Parse(g.Value);
        var foreground = PerceivedBrightness(color) > 130 ? Brushes.Black : Brushes.White;
        var toolTip = new ToolTip
        {
            Background = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)),
            Foreground = foreground,
            BorderBrush = foreground,
            Content = $"#{g.Value}"
        };
        ToolTip.SetTip(editor, toolTip);
        ToolTip.SetIsOpen(editor, true);
        e.Handled = true;
    }

    public static void Hide(TextEditor editor)
    {
        ToolTip.SetIsOpen(editor, false);
        ToolTip.SetTip(editor, null);
    }

    private static int PerceivedBrightness(SKColor c)
    {
        return (int) Math.Sqrt(
            c.Red * c.Red * .299 +
            c.Green * c.Green * .587 +
            c.Blue * c.Blue * .114);
    }
}
