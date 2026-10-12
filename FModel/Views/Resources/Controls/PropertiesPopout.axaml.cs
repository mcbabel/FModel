using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using FModel.Framework.Ui;
using FModel.ViewModels;

namespace FModel.Views.Resources.Controls;

public partial class PropertiesPopout : FWindow
{
    private readonly JsonFoldingStrategies _manager;
    private bool _isKDown; // Ctrl+K chords

    public PropertiesPopout(TabItem contextViewModel)
    {
        InitializeComponent();

        var area = ScreenInfo.PrimaryWorkingAreaDips;
        Width = area.Width * 0.40;
        Height = area.Height * 0.60;

        MyAvalonEditor.Document = new TextDocument
        {
            Text = contextViewModel.Document.Text,
            FileName = contextViewModel.Entry.PathWithoutExtension
        };
        MyAvalonEditor.FontSize = contextViewModel.FontSize;
        MyAvalonEditor.SyntaxHighlighting = contextViewModel.Highlighter;
        MyAvalonEditor.TextArea.TextView.LinkTextBackgroundBrush = null;
        MyAvalonEditor.TextArea.TextView.LinkTextForegroundBrush = Brushes.Cornsilk;
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new GamePathElementGenerator());
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new JumpElementGenerator());
        MyAvalonEditor.TextArea.TextView.ElementGenerators.Add(new HexColorElementGenerator());
        _manager = new JsonFoldingStrategies(MyAvalonEditor);
        _manager.UpdateFoldings(MyAvalonEditor.Document);

        var scrollPosition = contextViewModel.ScrollPosition;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => MyAvalonEditor.ScrollToVerticalOffset(scrollPosition), DispatcherPriority.Background);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        MyAvalonEditor.AddHandler(PointerWheelChangedEvent, OnPreviewMouseWheel, RoutingStrategies.Tunnel);
    }

    private void OnMouseHover(object sender, PointerEventArgs e) => HexColorToolTip.Show(MyAvalonEditor, e);

    private void OnMouseHoverStopped(object sender, PointerEventArgs e) => HexColorToolTip.Hide(MyAvalonEditor);

    private void OnPreviewMouseWheel(object sender, PointerWheelEventArgs e)
    {
        if (sender is not TextEditor avalonEditor || e.KeyModifiers != KeyModifiers.Control)
            return;

        var fontSize = avalonEditor.FontSize + e.Delta.Y * 120 / 50.0; // WPF delta is 120 per notch

        avalonEditor.FontSize = fontSize switch
        {
            < 6 => 6,
            > 200 => 200,
            _ => fontSize
        };
        e.Handled = true;
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K) _isKDown = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.K:
                _isKDown = true;
                break;
            case Key.J when _isKDown && ctrl:
                _manager.UnfoldAll();
                break;
            case Key.L when _isKDown && ctrl:
                _manager.FoldToggle(MyAvalonEditor.CaretOffset);
                break;
            case >= Key.D0 and <= Key.D9 when _isKDown && ctrl:
                _manager.FoldToggleAtLevel(int.Parse(e.Key.ToString()[1].ToString()));
                break;
        }
    }
}
