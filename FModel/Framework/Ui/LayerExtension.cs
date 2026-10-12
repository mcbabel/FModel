using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace FModel.Framework.Ui;

/// <summary>
/// Port of AdonisUI's LayerExtension: controls get different background/border brushes
/// depending on how deeply they are nested in "layered" containers (GroupBox, etc.).
/// <para>Layer: explicit layer of the element, its logical children get Layer + 1.</para>
/// <para>IncreaseLayer: logical children get ComputedLayer + 1.</para>
/// <para>ComputedLayer: the effective (inherited) layer, used by styles via
/// <c>^[(ui|LayerExtension.ComputedLayer)=2]</c> selectors.</para>
/// </summary>
public sealed class LayerExtension
{
    public static readonly AttachedProperty<int?> LayerProperty =
        AvaloniaProperty.RegisterAttached<LayerExtension, Control, int?>("Layer");

    public static readonly AttachedProperty<bool> IncreaseLayerProperty =
        AvaloniaProperty.RegisterAttached<LayerExtension, Control, bool>("IncreaseLayer");

    public static readonly AttachedProperty<int> ComputedLayerProperty =
        AvaloniaProperty.RegisterAttached<LayerExtension, Control, int>("ComputedLayer", 1, inherits: true);

    private static readonly AttachedProperty<bool> IsHookedProperty =
        AvaloniaProperty.RegisterAttached<LayerExtension, Control, bool>("IsHooked");

    public static int? GetLayer(Control obj) => obj.GetValue(LayerProperty);
    public static void SetLayer(Control obj, int? value) => obj.SetValue(LayerProperty, value);
    public static bool GetIncreaseLayer(Control obj) => obj.GetValue(IncreaseLayerProperty);
    public static void SetIncreaseLayer(Control obj, bool value) => obj.SetValue(IncreaseLayerProperty, value);
    public static int GetComputedLayer(Control obj) => obj.GetValue(ComputedLayerProperty);
    public static void SetComputedLayer(Control obj, int value) => obj.SetValue(ComputedLayerProperty, value);

    static LayerExtension()
    {
        LayerProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is int layer)
                c.SetValue(ComputedLayerProperty, layer);
            Hook(c);
            UpdateChildren(c);
        });
        IncreaseLayerProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            Hook(c);
            UpdateChildren(c);
        });
        ComputedLayerProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            if (c.GetValue(LayerProperty) != null || c.GetValue(IncreaseLayerProperty))
                UpdateChildren(c);
        });
    }

    private static void Hook(Control c)
    {
        if (c.GetValue(IsHookedProperty)) return;
        c.SetValue(IsHookedProperty, true);
        c.AttachedToLogicalTree += (_, _) => UpdateChildren(c);
        if (c is ILogical logical && logical.LogicalChildren is INotifyCollectionChanged incc)
            incc.CollectionChanged += (_, _) => UpdateChildren(c);
    }

    private static void UpdateChildren(Control c)
    {
        int value;
        if (c.GetValue(LayerProperty) is int layer)
            value = layer + 1;
        else if (c.GetValue(IncreaseLayerProperty))
            value = c.GetValue(ComputedLayerProperty) + 1;
        else
            return;

        foreach (var child in ((ILogical) c).LogicalChildren)
        {
            if (child is Control childControl && childControl.GetValue(LayerProperty) == null)
                childControl.SetValue(ComputedLayerProperty, value);
        }
    }
}
