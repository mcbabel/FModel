using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace FModel.Views.Resources.Controls.Explorer;

/// <summary>
/// Virtualizing, recycling wrap panel for items of uniform size (replaces the WPF VirtualizingWrapPanel NuGet with
/// Orientation="Horizontal" SpacingMode="Uniform"): only the rows inside the viewport get containers, so folders
/// with tens of thousands of packages stay responsive. The item size is taken from the first item's container.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel
{
    private const int BufferRows = 1;

    private readonly SortedDictionary<int, Control> _realized = new();
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();
    private static readonly object _noRecycleKey = new();

    private Rect _viewport;
    private Size _itemSize;
    private int _columns = 1;
    private (int First, int Last) _visibleRows = (-1, -1);
    private int _scrollToIndex = -1;

    public VirtualizingWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    private int ItemCount => Items.Count;

    private void OnEffectiveViewportChanged(object sender, EffectiveViewportChangedEventArgs e)
    {
        _viewport = e.EffectiveViewport;
        if (ComputeVisibleRows() != _visibleRows)
            InvalidateMeasure();
    }

    private (int First, int Last) ComputeVisibleRows()
    {
        if (_itemSize.Height <= 0) return (0, 0);

        var height = _viewport.Height > 0 ? _viewport.Height : Math.Max(Bounds.Height, 1080);
        var top = Math.Max(0, _viewport.Top);
        var first = Math.Max(0, (int) Math.Floor(top / _itemSize.Height) - BufferRows);
        var last = (int) Math.Ceiling((top + height) / _itemSize.Height) - 1 + BufferRows;
        return (first, Math.Max(first, last));
    }

    private int ComputeColumns(double width)
    {
        if (_itemSize.Width <= 0) return 1;
        if (double.IsInfinity(width) || double.IsNaN(width))
            width = _viewport.Width > 0 ? _viewport.Width : _itemSize.Width;
        return Math.Max(1, (int) Math.Floor(width / _itemSize.Width));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = ItemCount;
        if (count == 0)
        {
            RecycleAll();
            _itemSize = default;
            _visibleRows = (-1, -1);
            return default;
        }

        if (_itemSize.Width <= 0 || _itemSize.Height <= 0)
        {
            var first = GetOrRealize(0);
            first.Measure(Size.Infinity);
            _itemSize = first.DesiredSize;
            if (_itemSize.Width <= 0 || _itemSize.Height <= 0)
                return default;
        }

        _columns = ComputeColumns(availableSize.Width);
        var rows = (count + _columns - 1) / _columns;
        _visibleRows = ComputeVisibleRows();

        var firstIndex = Math.Min(count, _visibleRows.First * _columns);
        var lastIndex = Math.Min(count - 1, (_visibleRows.Last + 1) * _columns - 1);

        foreach (var (index, container) in _realized.ToArray())
        {
            if (index >= firstIndex && index <= lastIndex) continue;
            if (index == _scrollToIndex || container.IsKeyboardFocusWithin) continue;
            Recycle(index, container);
        }

        for (var i = firstIndex; i <= lastIndex; i++)
            GetOrRealize(i).Measure(_itemSize);
        foreach (var (index, container) in _realized)
        {
            if (index < firstIndex || index > lastIndex)
                container.Measure(_itemSize);
        }

        var width = double.IsInfinity(availableSize.Width) ? _columns * _itemSize.Width : availableSize.Width;
        return new Size(width, rows * _itemSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_itemSize.Width <= 0) return finalSize;

        _columns = ComputeColumns(finalSize.Width);
        // SpacingMode="Uniform": the free horizontal space is distributed evenly around the items
        var spacing = Math.Max(0, (finalSize.Width - _columns * _itemSize.Width) / (_columns + 1));
        foreach (var (index, container) in _realized)
            container.Arrange(GetItemRect(index, spacing));

        return finalSize;
    }

    private Rect GetItemRect(int index, double spacing)
    {
        var row = index / _columns;
        var column = index % _columns;
        return new Rect(spacing + column * (_itemSize.Width + spacing), row * _itemSize.Height, _itemSize.Width, _itemSize.Height);
    }

    private Control GetOrRealize(int index)
    {
        if (_realized.TryGetValue(index, out var existing))
            return existing;

        var generator = ItemContainerGenerator!;
        var item = Items[index];
        Control container;
        if (generator.NeedsContainer(item, index, out var recycleKey))
        {
            var key = recycleKey ?? _noRecycleKey;
            if (_recyclePool.TryGetValue(key, out var pool) && pool.Count > 0)
            {
                container = pool.Pop();
                container.IsVisible = true;
            }
            else
            {
                container = generator.CreateContainer(item, index, recycleKey);
                container.SetValue(RecycleKeyProperty, key);
                AddInternalChild(container);
            }

            generator.PrepareItemContainer(container, item, index);
            _realized[index] = container;
            generator.ItemContainerPrepared(container, item, index);
        }
        else
        {
            container = (Control) item!;
            container.SetValue(RecycleKeyProperty, null);
            AddInternalChild(container);
            _realized[index] = container;
            generator.ItemContainerPrepared(container, item, index);
        }

        return container;
    }

    private static readonly AttachedProperty<object> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingWrapPanel, Control, object>("RecycleKey");

    private void Recycle(int index, Control container)
    {
        _realized.Remove(index);
        if (container.GetValue(RecycleKeyProperty) is not { } key)
        {
            // the item is its own container
            RemoveInternalChild(container);
            return;
        }

        ItemContainerGenerator!.ClearItemContainer(container);
        container.IsVisible = false;
        if (!_recyclePool.TryGetValue(key, out var pool))
            _recyclePool[key] = pool = new Stack<Control>();
        pool.Push(container);
    }

    private void RecycleAll()
    {
        foreach (var (index, container) in _realized.ToArray())
            Recycle(index, container);
    }

    protected override void OnItemsChanged(IReadOnlyList<object> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        // containers are cheap to re-prepare compared to tracking index shifts, and the explorer sources reset anyway
        RecycleAll();
        _scrollToIndex = -1;
        if (items.Count == 0) _itemSize = default;
        InvalidateMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl oldValue)
    {
        base.OnItemsControlChanged(oldValue);
        RecycleAll();
        foreach (var pool in _recyclePool.Values)
        foreach (var container in pool)
            RemoveInternalChild(container);
        _recyclePool.Clear();
        _itemSize = default;
        InvalidateMeasure();
    }

    protected override Control ScrollIntoView(int index)
    {
        if (index < 0 || index >= ItemCount || !IsEffectivelyVisible)
            return null;

        _scrollToIndex = index;
        var container = GetOrRealize(index);
        InvalidateMeasure();
        UpdateLayout();

        var spacing = Math.Max(0, (Bounds.Width - _columns * _itemSize.Width) / (_columns + 1));
        this.BringIntoView(GetItemRect(index, spacing));
        UpdateLayout();

        _scrollToIndex = -1;
        return _realized.TryGetValue(index, out var result) ? result : container;
    }

    protected override Control ContainerFromIndex(int index)
        => _realized.TryGetValue(index, out var container) ? container : null;

    protected override int IndexFromContainer(Control container)
    {
        foreach (var (index, realized) in _realized)
        {
            if (ReferenceEquals(realized, container))
                return index;
        }

        return -1;
    }

    protected override IEnumerable<Control> GetRealizedContainers() => _realized.Values;

    protected override IInputElement GetControl(NavigationDirection direction, IInputElement from, bool wrap)
    {
        var count = ItemCount;
        if (count == 0) return null;

        var fromIndex = from is Control c ? IndexFromContainer(c) : -1;
        var rowsPerPage = _itemSize.Height > 0 && _viewport.Height > 0
            ? Math.Max(1, (int) Math.Floor(_viewport.Height / _itemSize.Height))
            : 1;

        var toIndex = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => count - 1,
            NavigationDirection.Left or NavigationDirection.Previous => fromIndex - 1,
            NavigationDirection.Right or NavigationDirection.Next => fromIndex + 1,
            NavigationDirection.Up => fromIndex < 0 ? 0 : fromIndex - _columns,
            NavigationDirection.Down => fromIndex < 0 ? 0 : fromIndex + _columns,
            NavigationDirection.PageUp => Math.Max(0, fromIndex - _columns * rowsPerPage),
            NavigationDirection.PageDown => Math.Min(count - 1, fromIndex + _columns * rowsPerPage),
            _ => -1
        };

        if (toIndex < 0 || toIndex >= count)
        {
            if (!wrap) return null;
            toIndex = toIndex < 0 ? count - 1 : 0;
        }

        return ScrollIntoView(toIndex);
    }
}
