using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Tfx;

/// <summary>
/// <see cref="ObservableCollection{T}"/> with a bulk swap that raises a single
/// Reset instead of one CollectionChanged per row. When the bound
/// ListCollectionView has a CustomSort active, each Add costs a sorted insert
/// (O(n) shift, O(n²) for a whole folder) and invalidates the view every time;
/// a Reset sorts once and re-renders once, so loading a large folder stays
/// flat regardless of the active column sort.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> newItems)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in newItems)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
