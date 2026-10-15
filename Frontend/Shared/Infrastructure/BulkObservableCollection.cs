using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// ObservableCollection that can replace all its items with ONE Reset notification instead of one per item
/// (thousands of Add notifications are what makes a bound list freeze).
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
