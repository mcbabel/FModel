using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace FModel.Framework;

/// <summary>Read-only concatenation of several observable lists (replacement for WPF's CompositeCollection).</summary>
public class CompositeCollection : IReadOnlyList<object>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly IList[] _sources;
    private List<object> _items = [];

    public CompositeCollection(params IList[] sources)
    {
        _sources = sources;
        foreach (var source in sources)
            if (source is INotifyCollectionChanged incc)
                incc.CollectionChanged += (_, _) => Rebuild();
        _items = _sources.SelectMany(s => s.Cast<object>()).ToList();
    }

    private void Rebuild()
    {
        _items = _sources.SelectMany(s => s.Cast<object>()).ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public event NotifyCollectionChangedEventHandler CollectionChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public int Count => _items.Count;
    public object this[int index] { get => _items[index]; set => throw new System.NotSupportedException(); }
    public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public bool Contains(object value) => _items.Contains(value);
    public int IndexOf(object value) => _items.IndexOf(value);
    public void CopyTo(System.Array array, int index) => ((ICollection) _items).CopyTo(array, index);
    public bool IsReadOnly => true;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object value) => throw new System.NotSupportedException();
    public void Clear() => throw new System.NotSupportedException();
    public void Insert(int index, object value) => throw new System.NotSupportedException();
    public void Remove(object value) => throw new System.NotSupportedException();
    public void RemoveAt(int index) => throw new System.NotSupportedException();
}
