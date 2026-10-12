using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FModel.Framework.Ui;
using FModel.Settings;
using FModel.Views.Resources.Controls;
using Serilog;
using SkiaSharp;
using OpenFileDialog = FModel.Framework.Ui.OpenFileDialog;
using SaveFileDialog = FModel.Framework.Ui.SaveFileDialog;

namespace FModel.Views;

public partial class ImageMerger : FWindow
{
    /// <summary>An image of the merge list (WPF stored the path in ListBoxItem.ContentStringFormat).</summary>
    public sealed class ImageItem
    {
        public string FilePath { get; init; }
        public string Name { get; init; }
        public override string ToString() => Name;
    }

    private const string FILENAME = "Preview.png";
    private static readonly string[] _imageExtensions = [".png", ".bmp", ".jpg", ".jpeg", ".jfif", ".jpe", ".tiff", ".tif"];

    private readonly ObservableCollection<ImageItem> _images = [];
    private byte[] _imageBuffer;
    private Bitmap _preview;

    public ImageMerger()
    {
        InitializeComponent();
        ImagesListBox.ItemsSource = _images;

        // WPF: Height = 65% of the screen height, Width = 55% of the screen width
        var screen = ScreenInfo.PrimaryWorkingAreaDips;
        Height = Math.Round(screen.Height * 0.65);
        Width = Math.Round(screen.Width * 0.55);

        // WPF: MouseUp + Thumb.DragCompleted on both sliders redraw the preview once the value is final
        SizeSlider.AddHandler(PointerReleasedEvent, OnSliderReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        MarginSlider.AddHandler(PointerReleasedEvent, OnSliderReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnSliderReleased(object sender, PointerReleasedEventArgs e)
    {
        if (ImagePreview.Source != null)
            await DrawPreview();
    }

    private void SetCommandsEnabled(bool enabled)
    {
        AddButton.IsEnabled = enabled;
        UpButton.IsEnabled = enabled;
        DownButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enabled;
        ClearButton.IsEnabled = enabled;
        SizeSlider.IsEnabled = enabled;
        OpenImageButton.IsEnabled = enabled;
        SaveImageButton.IsEnabled = enabled;
    }

    private async Task DrawPreview()
    {
        if (_images.Count == 0)
        {
            ClearPreview();
            return;
        }

        SetCommandsEnabled(false);
        try
        {
            var margin = UserSettings.Default.ImageMergerMargin;
            var imagesPerRow = Math.Max(1, Convert.ToInt32(SizeSlider.Value));
            var paths = _images.Select(x => x.FilePath).ToArray();

            var (buffer, bitmap) = await Task.Run(() => Merge(paths, imagesPerRow, margin));
            if (buffer == null) return;

            _imageBuffer = buffer;
            ImagePreview.Source = _preview = bitmap; // previous bitmap may still be shown in the popout, let the GC collect it
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to merge images");
            FLogger.Append(ELog.Error, () => FLogger.Text($"Failed to merge images: {e.Message}", Constants.WHITE, true));
        }
        finally
        {
            SetCommandsEnabled(true);
        }
    }

    private static (byte[], Bitmap) Merge(string[] paths, int imagesPerRow, int margin)
    {
        int num = 1, curW = 0, curH = 0, maxWidth = 0, maxHeight = 0, lineMaxHeight = 0;
        var positions = new List<SKPoint>();
        var images = new List<SKBitmap>();
        try
        {
            foreach (var path in paths)
            {
                SKBitmap image;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    image = SKBitmap.Decode(stream);

                if (image == null) // SkiaSharp cannot decode TIFF files (WPF used System.Drawing for those)
                {
                    Log.Warning("Unsupported image format \"{Path}\"", path);
                    FLogger.Append(ELog.Warning, () => FLogger.Text($"Could not decode '{Path.GetFileName(path)}', unsupported image format", Constants.WHITE, true));
                    continue;
                }

                positions.Add(new SKPoint(curW, curH));
                images.Add(image);

                if (image.Height > lineMaxHeight)
                    lineMaxHeight = image.Height;

                if (num % imagesPerRow == 0)
                {
                    maxWidth = curW + image.Width + margin;
                    curH += lineMaxHeight + margin;
                    if (curH > maxHeight)
                        maxHeight = curH;

                    curW = 0;
                    lineMaxHeight = 0;
                }
                else
                {
                    maxHeight = curH + lineMaxHeight + margin;
                    curW += image.Width + margin;
                    if (curW > maxWidth)
                        maxWidth = curW;
                }

                num++;
            }

            if (images.Count == 0 || maxWidth - margin <= 0 || maxHeight - margin <= 0)
                return (null, null);

            using var bmp = new SKBitmap(maxWidth - margin, maxHeight - margin, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };

            for (var i = 0; i < images.Count; i++)
                canvas.DrawBitmap(images[i], positions[i], paint);

            using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
            var buffer = data.ToArray();
            using var ms = new MemoryStream(buffer);
            return (buffer, new Bitmap(ms));
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
        }
    }

    private void ClearPreview()
    {
        ImagePreview.Source = null;
        _preview = null;
        _imageBuffer = null;
    }

    private async Task AddImages(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            _images.Add(new ImageItem
            {
                FilePath = file,
                Name = Path.GetFileNameWithoutExtension(file)
            });
        }

        SizeSlider.Value = Math.Min(_images.Count, Math.Round(Math.Sqrt(_images.Count)));
        await DrawPreview();
    }

    private async void OnImageAdd(object sender, RoutedEventArgs e)
    {
        var fileBrowser = new OpenFileDialog
        {
            Title = "Add image(s)",
            InitialDirectory = Path.Combine(UserSettings.Default.OutputDirectory, "Exports"),
            Multiselect = true,
            Filter = "Image Files (*.png,*.bmp,*.jpg,*.jpeg,*.jfif,*.jpe,*.tiff,*.tif)|*.png;*.bmp;*.jpg;*.jpeg;*.jfif;*.jpe;*.tiff;*.tif|All Files (*.*)|*.*"
        };
        var result = fileBrowser.ShowDialog();
        if (!result.HasValue || !result.Value) return;

        await AddImages(fileBrowser.FileNames);
    }

    private static string[] GetDroppedImages(DragEventArgs e)
    {
        return e.Data.GetFiles()?
            .Select(x => x.TryGetLocalPath())
            .Where(p => p != null && File.Exists(p) && _imageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .ToArray() ?? [];
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = AddButton.IsEnabled && GetDroppedImages(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!AddButton.IsEnabled) return;

        var files = GetDroppedImages(e);
        if (files.Length > 0)
            await AddImages(files);
    }

    private async void ModifyItemInList(object sender, RoutedEventArgs e)
    {
        if (_images.Count <= 0 || ImagesListBox.SelectedItems is not { Count: > 0 } selectedItems) return;
        var selected = selectedItems.Cast<ImageItem>().ToList();
        var indices = selected.Select(_images.IndexOf).OrderBy(i => i).ToArray();
        var reloadImage = false;

        switch (((Button) sender).Name)
        {
            case "UpButton":
            {
                if (indices[0] > 0)
                {
                    foreach (var i in indices)
                        _images.Move(i, i - 1);
                    reloadImage = true;
                }

                break;
            }
            case "DownButton":
            {
                if (indices[^1] < _images.Count - 1)
                {
                    foreach (var i in indices.Reverse())
                        _images.Move(i, i + 1);
                    reloadImage = true;
                }

                break;
            }
            case "DeleteButton":
            {
                foreach (var item in selected)
                    _images.Remove(item);
                selected.Clear();
                reloadImage = true;
                break;
            }
        }

        // keep the moved items selected
        ImagesListBox.SelectedItems?.Clear();
        foreach (var item in selected)
            ImagesListBox.SelectedItems?.Add(item);

        if (reloadImage)
            await DrawPreview();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _images.Clear();
        ClearPreview();
    }

    private void OnOpenImage(object sender, RoutedEventArgs e)
    {
        if (_preview == null) return;
        var size = _preview.Size;
        Helper.OpenWindow<FWindow>("Merged Image", () =>
        {
            var popout = new ImagePopout
            {
                Title = "Merged Image",
                Width = size.Width,
                Height = size.Height,
                WindowState = size.Height > 1000 ? WindowState.Maximized : WindowState.Normal
            };
            popout.ImageCtrl.Source = _preview;
            popout.Show();
        });
    }

    private void OnSaveImage(object sender, RoutedEventArgs e)
    {
        if (ImagePreview.Source == null || _imageBuffer == null) return;
        var saveFileDialog = new SaveFileDialog
        {
            Title = "Save Image",
            FileName = FILENAME,
            InitialDirectory = UserSettings.Default.OutputDirectory,
            Filter = "Png Files (*.png)|*.png|All Files (*.*)|*.*"
        };
        var result = saveFileDialog.ShowDialog();
        if (!result.HasValue || !result.Value) return;

        using (var fs = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            fs.Write(_imageBuffer, 0, _imageBuffer.Length);
        }

        SaveCheck(saveFileDialog.FileName, Path.GetFileName(saveFileDialog.FileName));
    }

    private static void SaveCheck(string path, string fileName)
    {
        if (File.Exists(path))
        {
            Log.Information("{FileName} successfully saved", fileName);
            FLogger.Append(ELog.Information, () =>
            {
                FLogger.Text("Successfully saved ", Constants.WHITE);
                FLogger.Link(fileName, path, true);
            });
        }
        else
        {
            Log.Error("{FileName} could not be saved", fileName);
            FLogger.Append(ELog.Error, () => FLogger.Text($"Could not save '{fileName}'", Constants.WHITE, true));
        }
    }

    private void OnCopyImage(object sender, RoutedEventArgs e)
    {
        if (_imageBuffer == null) return;
        FModel.Framework.Ui.Clipboard.SetImage(_imageBuffer, FILENAME);
    }
}
