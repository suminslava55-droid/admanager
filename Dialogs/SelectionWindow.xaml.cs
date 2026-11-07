using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using AdManager.Common;

namespace AdManager.Dialogs;

public partial class SelectionWindow : Window
{
    private readonly ObservableCollection<PickerItem> _items;
    private readonly ICollectionView _view;

    public IReadOnlyList<object> SelectedValues { get; private set; } = Array.Empty<object>();

    public SelectionWindow(string title, string hint, IEnumerable<PickerItem> items, string searchHint = "Поиск…")
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        Title = title;
        HeaderText.Text = title;
        HintText.Text = hint;
        SearchBox.ToolTip = searchHint;

        _items = new ObservableCollection<PickerItem>(items);
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = Filter;
        ItemsList.ItemsSource = _view;

        foreach (var item in _items)
        {
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PickerItem.IsSelected))
                {
                    UpdateCount();
                }
            };
        }

        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private bool Filter(object obj)
    {
        if (obj is not PickerItem item) return false;
        string query = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        return query.Length == 0 || item.SearchBlob.Contains(query, StringComparison.Ordinal);
    }

    private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _view.Refresh();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (PickerItem item in _view)
        {
            item.IsSelected = true;
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }
    }

    private void UpdateCount()
    {
        int count = _items.Count(i => i.IsSelected);
        CountText.Text = $"Выбрано: {count} из {_items.Count}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedValues = _items.Where(i => i.IsSelected).Select(i => i.Value).ToList();
        DialogResult = true;
    }
}
