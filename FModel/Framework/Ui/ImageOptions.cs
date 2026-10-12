using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FModel.Framework.Ui;

/// <summary>Bindable replacement for RenderOptions.BitmapInterpolationMode (which can't be bound in Avalonia 11).</summary>
public sealed class ImageOptions
{
    public static readonly AttachedProperty<bool> NearestNeighborProperty =
        AvaloniaProperty.RegisterAttached<ImageOptions, Visual, bool>("NearestNeighbor");

    public static bool GetNearestNeighbor(Visual visual) => visual.GetValue(NearestNeighborProperty);
    public static void SetNearestNeighbor(Visual visual, bool value) => visual.SetValue(NearestNeighborProperty, value);

    static ImageOptions()
    {
        NearestNeighborProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            RenderOptions.SetBitmapInterpolationMode(visual, e.NewValue is true ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            visual.InvalidateVisual();
        });
    }
}
