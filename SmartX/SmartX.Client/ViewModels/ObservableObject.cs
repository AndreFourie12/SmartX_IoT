using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartX.Client.ViewModels
{
    /// <summary>
    /// Minimal INotifyPropertyChanged base. Rows on the Command Stream page update their properties in
    /// place every second rather than being rebuilt, which keeps the list's selection, scroll position
    /// and hover state steady while the values underneath them change.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Reconciles an ObservableCollection of view models against a fresh list from the API, by key:
    /// existing rows are updated in place, new ones inserted, missing ones removed, and the order
    /// corrected with Move. A Dictionary of the current rows makes each match O(1).
    /// </summary>
    public static class CollectionSync
    {
        public static void Sync<TDto, TViewModel>(
            ObservableCollection<TViewModel> target,
            IReadOnlyList<TDto> source,
            Func<TDto, string> dtoKey,
            Func<TViewModel, string> viewModelKey,
            Func<TDto, TViewModel> create,
            Action<TViewModel, TDto> update)
        {
            var existing = target.ToDictionary(viewModelKey, StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(source.Select(dtoKey), StringComparer.OrdinalIgnoreCase);

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(viewModelKey(target[i])))
                    target.RemoveAt(i);
            }

            for (int index = 0; index < source.Count; index++)
            {
                var dto = source[index];
                string key = dtoKey(dto);

                if (existing.TryGetValue(key, out var viewModel))
                {
                    update(viewModel, dto);

                    int current = target.IndexOf(viewModel);
                    if (current != index)
                        target.Move(current, index);
                }
                else
                {
                    target.Insert(index, create(dto));
                }
            }
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
