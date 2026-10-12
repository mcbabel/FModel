using FModel.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace FModel.Framework;

public readonly record struct SortDescription(string PropertyName, ListSortDirection Direction);

/// <summary>
/// Lightweight replacement for WPF's ListCollectionView: a filtered + sorted live view
/// over a source list that can be bound as ItemsSource.
/// </summary>
public class ListCollectionView : IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly IList _source;
    private List<object> _view;
    private Predicate<object> _filter;
    private readonly Dictionary<string, PropertyInfo> _propertyCache = new();
    private bool _isLiveSorting;

    public ObservableCollection<SortDescription> SortDescriptions { get; } = [];

    public ListCollectionView(IList source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _view = [.._source.Cast<object>()];
        if (_source is INotifyCollectionChanged incc)
            incc.CollectionChanged += OnSourceCollectionChanged;
        SortDescriptions.CollectionChanged += (_, _) => Refresh();
    }

    public IList SourceCollection => _source;

    public Predicate<object> Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    /// <summary>Re-sort items when a property used for sorting changes.</summary>
    public bool IsLiveSorting
    {
        get => _isLiveSorting;
        set => _isLiveSorting = value;
    }

    public IComparer CustomSort { get; set; }

    public int Count => _view.Count;

    public bool IsEmpty => _view.Count == 0;

    private bool HasSortOrFilter => _filter != null || SortDescriptions.Count > 0 || CustomSort != null;

    public void Refresh()
    {
        IEnumerable<object> items = _source.Cast<object>();
        if (_filter != null)
            items = items.Where(x => _filter(x));

        var list = items.ToList();
        if (SortDescriptions.Count > 0 || CustomSort != null)
            list.Sort(Compare);

        _view = list;
        RaiseReset();
    }

    private int Compare(object a, object b)
    {
        if (CustomSort != null)
            return CustomSort.Compare(a, b);

        foreach (var sd in SortDescriptions)
        {
            var va = GetValue(a, sd.PropertyName) as IComparable;
            var vb = GetValue(b, sd.PropertyName);
            int r;
            if (va == null) r = vb == null ? 0 : -1;
            else if (vb == null) r = 1;
            else if (va is string sa && vb is string sb) r = string.Compare(sa, sb, StringComparison.CurrentCulture);
            else r = va.CompareTo(vb);
            if (r != 0) return sd.Direction == ListSortDirection.Ascending ? r : -r;
        }
        return 0;
    }

    private object GetValue(object item, string path)
    {
        if (item == null) return null;
        if (string.IsNullOrEmpty(path)) return item;
        foreach (var part in path.Split('.'))
        {
            if (item == null) return null;
            var key = item.GetType().FullName + "." + part;
            if (!_propertyCache.TryGetValue(key, out var prop))
            {
                prop = item.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
                _propertyCache[key] = prop;
            }
            item = prop?.GetValue(item);
        }
        return item;
    }

    private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!HasSortOrFilter)
        {
            // 1:1 mirror of the source, forward the event as is
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null && e.NewStartingIndex >= 0:
                    _view.InsertRange(e.NewStartingIndex, e.NewItems.Cast<object>());
                    RaiseCountChanged();
                    CollectionChanged?.Invoke(this, e);
                    return;
                case NotifyCollectionChangedAction.Remove when e.OldItems != null && e.OldStartingIndex >= 0:
                    _view.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
                    RaiseCountChanged();
                    CollectionChanged?.Invoke(this, e);
                    return;
                case NotifyCollectionChangedAction.Replace when e.NewItems != null && e.NewStartingIndex >= 0:
                    for (var i = 0; i < e.NewItems.Count; i++)
                        _view[e.NewStartingIndex + i] = e.NewItems[i];
                    CollectionChanged?.Invoke(this, e);
                    return;
                default:
                    _view = [.._source.Cast<object>()];
                    RaiseReset();
                    return;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: 1 })
        {
            var item = e.NewItems[0];
            if (_filter != null && !_filter(item))
                return;

            var index = _view.Count;
            if (SortDescriptions.Count > 0 || CustomSort != null)
            {
                var lo = 0;
                var hi = _view.Count;
                while (lo < hi)
                {
                    var mid = (lo + hi) / 2;
                    if (Compare(_view[mid], item) <= 0) lo = mid + 1;
                    else hi = mid;
                }
                index = lo;
            }

            _view.Insert(index, item);
            RaiseCountChanged();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is { Count: 1 })
        {
            var item = e.OldItems[0];
            var index = _view.IndexOf(item);
            if (index < 0) return;
            _view.RemoveAt(index);
            RaiseCountChanged();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            return;
        }

        Refresh();
    }

    private void RaiseReset()
    {
        RaiseCountChanged();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void RaiseCountChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public IEnumerator GetEnumerator() => _view.GetEnumerator();
    public object this[int index]
    {
        get => _view[index];
        set => throw new NotSupportedException();
    }

    public bool Contains(object value) => _view.Contains(value);
    public int IndexOf(object value) => _view.IndexOf(value);
    public void CopyTo(Array array, int index) => ((ICollection) _view).CopyTo(array, index);
    public bool IsReadOnly => true;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object value) => throw new NotSupportedException();
    public void Remove(object value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
