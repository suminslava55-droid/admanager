using System.Windows;
using System.Windows.Controls;
using AdManager.Common;
using AdManager.Models;

namespace AdManager.Dialogs;

public partial class CreateGroupWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _samBox;

    public NewGroupRequest? Request { get; private set; }

    public CreateGroupWindow(string parentDn, IReadOnlyList<AdOu> ous)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);

        var options = ous
            .Where(o => !string.IsNullOrWhiteSpace(o.DistinguishedName))
            .Select(o => new OuOption { Ou = o })
            .GroupBy(o => o.Ou.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        OuCombo.ItemsSource = options;
        OuCombo.SelectedItem = options.FirstOrDefault(o =>
            o.Ou.DistinguishedName.Equals(parentDn, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();

        var grid = FormBuilder.CreateGrid(2);
        _nameBox = FormBuilder.AddField(grid, 0, 0, "Имя группы", string.Empty);
        _samBox = FormBuilder.AddField(grid, 0, 1, "Логин группы", string.Empty);
        FieldsHost.Children.Add(grid);

        Loaded += (_, _) => _nameBox.Focus();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (OuCombo.SelectedItem is not OuOption option)
        {
            Fail("Выберите подразделение.");
            return;
        }

        string name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Fail("Заполните имя группы.");
            return;
        }

        string sam = _samBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sam)) sam = Truncate(name, 20);

        string scope = (ScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Global";

        Request = new NewGroupRequest
        {
            ParentDn = option.Ou.DistinguishedName,
            Name = name,
            SamAccountName = sam,
            Description = DescriptionBox.Text.Trim(),
            IsSecurity = SecurityCheck.IsChecked == true,
            Scope = scope
        };

        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
