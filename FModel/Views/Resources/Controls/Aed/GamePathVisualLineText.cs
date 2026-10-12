using System;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using CUE4Parse.Utils;
using FModel.Extensions;
using FModel.Services;
using FModel.ViewModels;
using AvaloniaEdit.Rendering;

namespace FModel.Views.Resources.Controls;

public class GamePathVisualLineText : VisualLineText
{
    private ThreadWorkerViewModel _threadWorkerView => ApplicationService.ThreadWorkerView;
    private ApplicationViewModel _applicationView => ApplicationService.ApplicationView;

    public delegate void GamePathOnClick(string gamePath);

    public event GamePathOnClick OnGamePathClicked;
    private readonly string _gamePath;

    public GamePathVisualLineText(string gamePath, VisualLine parentVisualLine, int length) : base(parentVisualLine, length)
    {
        _gamePath = gamePath;
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var relativeOffset = startVisualColumn - VisualColumn;
        var text = context.GetText(context.VisualLine.FirstDocumentLine.Offset + RelativeTextOffset + relativeOffset, DocumentLength - relativeOffset);

        if (text.Count != 2) // ": "
            TextRunProperties.SetForegroundBrush(Brushes.Plum);

        return new TextCharacters(text.Text.AsMemory(text.Offset, text.Count), TextRunProperties);
    }

    private bool GamePathIsClickable(KeyModifiers modifiers) => !string.IsNullOrEmpty(_gamePath) && modifiers == KeyModifiers.None;

    protected override void OnQueryCursor(PointerEventArgs e)
    {
        if (!GamePathIsClickable(e.KeyModifiers)) return;
        if (e.Source is InputElement inputElement)
            inputElement.Cursor = new Cursor(StandardCursorType.Hand);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed || !GamePathIsClickable(e.KeyModifiers))
            return;
        if (e.Handled || OnGamePathClicked == null)
            return;

        OnGamePathClicked(_gamePath);
        e.Handled = true;
    }

    protected override VisualLineText CreateInstance(int length)
    {
        var a = new GamePathVisualLineText(_gamePath, ParentVisualLine, length);
        a.OnGamePathClicked += async gamePath =>
        {
            var obj = gamePath.SubstringAfterLast('.');
            var package = gamePath.SubstringBeforeLast('.');
            var fullPath = _applicationView.CUE4Parse.Provider.FixPath(package);

            var firstLine = a.ParentVisualLine.Document.GetLineByNumber(2);
            if (a.ParentVisualLine.Document.FileName.Equals(fullPath.SubstringBeforeLast('.'), StringComparison.OrdinalIgnoreCase) &&
                !a.ParentVisualLine.Document.GetText(firstLine.Offset, firstLine.Length).Equals("  \"Summary\": {")) // Show Metadata case
            {
                var lineNumber = a.ParentVisualLine.Document.Text.GetNameLineNumber(obj);
                if (lineNumber > -1)
                {
                    var line = a.ParentVisualLine.Document.GetLineByNumber(lineNumber);
                    AvalonEditor.YesWeEditor.Select(line.Offset, line.Length);
                    AvalonEditor.YesWeEditor.ScrollToLine(lineNumber);
                    return;
                }
            }

            await _threadWorkerView.Begin(cancellationToken =>
                _applicationView.CUE4Parse.ExtractAndScroll(cancellationToken, fullPath, obj));
        };
        return a;
    }
}
