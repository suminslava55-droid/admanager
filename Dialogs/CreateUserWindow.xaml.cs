using System.Windows;
using System.Windows.Controls;
using AdManager.Common;
using AdManager.Models;
using AdManager.Services;

namespace AdManager.Dialogs;

public partial class CreateUserWindow : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<AdOu> _ous;

    public NewUserRequest? Request { get; private set; }

    public CreateUserWindow(string parentDn, IReadOnlyList<AdOu> ous, AdUser? template = null, int groupCount = 0)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        _ous = ous;

        var options = ous
            .Where(o => !string.IsNullOrWhiteSpace(o.DistinguishedName))
            .Select(o => new OuOption { Ou = o })
            .GroupBy(o => o.Ou.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        OuCombo.ItemsSource = options;
        OuCombo.SelectedItem = options.FirstOrDefault(o =>
            o.Ou.DistinguishedName.Equals(parentDn, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();

        BuildForm();
        PasswordBox.Text = PasswordGenerator.Generate(14);

        if (template is not null)
        {
            Title = "Копирование учётной записи";
            HeaderText.Text = "НОВАЯ УЗ НА ОСНОВЕ КОПИИ";
            HintText.Text = $"Поля организации скопированы из «{template.DisplayLabel}». Задайте ФИО, логин и пароль.";
            CopyInfoPanel.Visibility = Visibility.Visible;
            CopyInfoText.Text = groupCount > 0
                ? $"Будет скопировано членство в группах: {groupCount}."
                : "У исходной УЗ нет групп для копирования.";
            CopyGroupsCheck.IsChecked = groupCount > 0;
            CopyGroupsCheck.IsEnabled = groupCount > 0;
            PrefillFromTemplate(template);
        }

        Loaded += (_, _) => _boxes["samAccountName"].Focus();
    }

    private void PrefillFromTemplate(AdUser template)
    {
        SetField("title", template.Title);
        SetField("department", template.Department);
        SetField("company", template.Company);
        SetField("manager", template.ManagerDn);
        SetField("telephoneNumber", template.Phone);
        SetField("mobile", template.Mobile);
        SetField("physicalDeliveryOfficeName", template.Office);
        SetField("description", template.Description);
    }

    private void SetField(string attribute, string value)
    {
        if (_boxes.TryGetValue(attribute, out TextBox? box))
        {
            box.Text = value ?? string.Empty;
        }
    }

    private void BuildForm()
    {
        var fields = new (string Attribute, string Label)[]
        {
            ("cn", "Имя (CN)"),
            ("samAccountName", "Логин (sAMAccountName)"),
            ("userPrincipalName", "UPN"),
            ("displayName", "Отображаемое имя"),
            ("givenName", "Имя"),
            ("sn", "Фамилия"),
            ("mail", "Почта"),
            ("title", "Должность"),
            ("department", "Отдел"),
            ("company", "Организация"),
            ("manager", "Руководитель (DN или логин)"),
            ("telephoneNumber", "Телефон"),
            ("mobile", "Мобильный"),
            ("physicalDeliveryOfficeName", "Кабинет"),
            ("description", "Описание")
        };

        int rows = (fields.Length + 1) / 2;
        var grid = FormBuilder.CreateGrid(rows);
        for (int i = 0; i < fields.Length; i++)
        {
            TextBox box = FormBuilder.AddField(grid, i / 2, i % 2, fields[i].Label, string.Empty);
            _boxes[fields[i].Attribute] = box;
        }

        FieldsPanel.Children.Add(grid);
    }

    private string Value(string attribute)
        => _boxes.TryGetValue(attribute, out TextBox? box) ? box.Text.Trim() : string.Empty;

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        PasswordBox.Text = PasswordGenerator.Generate(14);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (OuCombo.SelectedItem is not OuOption option)
        {
            Fail("Выберите подразделение.");
            return;
        }

        string cn = Value("cn");
        string display = Value("displayName");
        if (string.IsNullOrWhiteSpace(cn) && string.IsNullOrWhiteSpace(display))
        {
            Fail("Заполните CN или отображаемое имя.");
            return;
        }

        string sam = Value("samAccountName");
        if (string.IsNullOrWhiteSpace(sam))
        {
            Fail("Заполните логин (sAMAccountName).");
            return;
        }

        if (PasswordBox.Text.Length < 7)
        {
            Fail("Пароль должен содержать не менее 7 символов.");
            return;
        }

        Request = new NewUserRequest
        {
            ParentDn = option.Ou.DistinguishedName,
            Cn = cn,
            DisplayName = display,
            SamAccountName = sam,
            UserPrincipalName = Value("userPrincipalName"),
            FirstName = Value("givenName"),
            LastName = Value("sn"),
            Mail = Value("mail"),
            Title = Value("title"),
            Department = Value("department"),
            Company = Value("company"),
            ManagerDn = Value("manager"),
            Phone = Value("telephoneNumber"),
            Mobile = Value("mobile"),
            Office = Value("physicalDeliveryOfficeName"),
            Description = Value("description"),
            Password = PasswordBox.Text,
            Enabled = EnabledCheck.IsChecked == true,
            MustChangePassword = MustChangeCheck.IsChecked == true,
            PasswordNeverExpires = NeverExpiresCheck.IsChecked == true,
            CopyGroups = CopyGroupsCheck.IsChecked == true
        };

        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
