using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdManager.Common;
using AdManager.Models;

namespace AdManager.Dialogs;

public partial class OuPickerWindow : Window
{
    private readonly ObservableCollection<AdOu> _roots = new();
    private readonly List<AdOu> _all;

    public AdOu? SelectedOu { get; private set; }

    public OuPickerWindow(string title, IReadOnlyList<AdOu> ous, string? currentDn)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        Title = title;
        HeaderText.Text = title;
        _all = ous.Where(o => !string.IsNullOrWhiteSpace(o.DistinguishedName)).ToList();

        var known = _all.Select(o => o.DistinguishedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ou in _all)
        {
            if (string.IsNullOrWhiteSpace(ou.ParentDn) || !known.Contains(ou.ParentDn))
            {
                _roots.Add(ou);
            }
        }

        if (_roots.Count == 0)
        {
            var root = _all.OrderBy(o => o.DistinguishedName.Length).FirstOrDefault();
            if (root is not null) _roots.Add(root);
        }

        OuTree.ItemsSource = _roots;

        AdOu? match = currentDn is null
            ? null
            : _all.FirstOrDefault(o => o.DistinguishedName.Equals(currentDn, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedOu = match;
            PathText.Text = match.DistinguishedName;
        }
        else
        {
            PathText.Text = "Выберите подразделение";
        }

        Loaded += (_, _) => OkButton.IsEnabled = SelectedOu is not null;
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = FilterBox.Text?.Trim() ?? string.Empty;
        var filtered = query.Length == 0
            ? _all
            : _all.Where(o => o.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                              || o.DistinguishedName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var allowed = filtered.Select(o => o.DistinguishedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        OuTree.ItemsSource = BuildFilteredTree(allowed);
    }

    private ObservableCollection<AdOu> BuildFilteredTree(HashSet<string> allowed)
    {
        var result = new ObservableCollection<AdOu>();
        foreach (var ou in _all)
        {
            bool self = allowed.Contains(ou.DistinguishedName);
            bool child = _all.Any(c => c.ParentDn.Equals(ou.DistinguishedName, StringComparison.OrdinalIgnoreCase)
                                       && allowed.Contains(c.DistinguishedName));
            if (!self && !child) continue;

            var copy = new AdOu
            {
                Name = ou.Name,
                DistinguishedName = ou.DistinguishedName,
                ParentDn = ou.ParentDn,
                Description = ou.Description,
                IsExpanded = true
            };

            result.Add(copy);
        }

        // Собираем иерархию из копий.
        var map = result.ToDictionary(o => o.DistinguishedName, StringComparer.OrdinalIgnoreCase);
        var roots = new ObservableCollection<AdOu>();
        foreach (var ou in result)
        {
            if (map.TryGetValue(ou.ParentDn, out var parent))
            {
                parent.Children.Add(ou);
            }
            else
            {
                roots.Add(ou);
            }
        }

        return roots;
    }

    private void OuTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectedOu = e.NewValue as AdOu;
        OkButton.IsEnabled = SelectedOu is not null;
        PathText.Text = SelectedOu?.DistinguishedName ?? "Выберите подразделение";
    }

    private void OuTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedOu is not null)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOu is null)
        {
            return;
        }

        DialogResult = true;
    }
}
