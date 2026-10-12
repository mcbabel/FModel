using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FModel.Views.Resources.Controls;

public partial class CommitControl : UserControl
{
    public CommitControl()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Attached property <c>RemoteImage.FillSource</c>: downloads an image URL (cached) and fills the shape with it
/// (WPF converted an URL string into an ImageBrush source on its own).
/// </summary>
public static class RemoteImage
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<string, Task<Bitmap>> _cache = new();

    public static readonly AttachedProperty<string> FillSourceProperty =
        AvaloniaProperty.RegisterAttached<Shape, string>("FillSource", typeof(RemoteImage));

    public static string GetFillSource(Shape shape) => shape.GetValue(FillSourceProperty);
    public static void SetFillSource(Shape shape, string value) => shape.SetValue(FillSourceProperty, value);

    static RemoteImage()
    {
        FillSourceProperty.Changed.AddClassHandler<Shape>(OnFillSourceChanged);
    }

    private static async void OnFillSourceChanged(Shape shape, AvaloniaPropertyChangedEventArgs e)
    {
        shape.Fill = null;
        if (e.NewValue is not string url || string.IsNullOrEmpty(url))
            return;

        var bitmap = await _cache.GetOrAdd(url, Download);
        if (bitmap == null || GetFillSource(shape) != url)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => shape.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill });
    }

    private static async Task<Bitmap> Download(string url)
    {
        try
        {
            var bytes = await _client.GetByteArrayAsync(url).ConfigureAwait(false);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }
}
