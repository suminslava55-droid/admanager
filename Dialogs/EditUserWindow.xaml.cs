using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdManager.Common;
using AdManager.Models;

namespace AdManager.Dialogs;

public partial class EditUserWindow : Window
{
    private readonly AdUser _user;
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _original = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?>? Attributes { get; private set; }

    public EditUserWindow(AdUser user)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        _user = user;
        HeaderText.Text = user.DisplayLabel + " · " + user.SamAccountName;
        Build();
    }

    private void Build()
    {
        AddSection("Основное",
            ("displayName", "Отображаемое имя"),
            ("givenName", "Имя"),
            ("sn", "Фамилия"),
            ("sAMAccountName", "Логин (sAMAccountName)"),
            ("userPrincipalName", "UPN"),
            ("mail", "Почта"));

        AddSection("Организация",
            ("title", "Должность"),
            ("department", "Отдел"),
            ("company", "Организация"),
            ("division", "Подразделение"),
            ("manager", "Руководитель (DN или логин)"),
            ("employeeID", "Табельный номер"),
            ("employeeType", "Тип сотрудника"));

        AddSection("Контакты",
            ("telephoneNumber", "Телефон"),
            ("mobile", "Мобильный"),
            ("ipPhone", "IP-телефон"),
            ("physicalDeliveryOfficeName", "Кабинет"),
            ("streetAddress", "Улица"),
            ("l", "Город"),
            ("st", "Область"),
            ("postalCode", "Индекс"),
            ("co", "Страна"),
            ("wWWHomePage", "Веб-сайт"));

        AddSection("Профиль и вход",
            ("profilePath", "Профиль"),
            ("homeDirectory", "Домашний каталог"),
            ("homeDrive", "Домашний диск"),
            ("scriptPath", "Сценарий входа"),
            ("userWorkstations", "Рабочие станции (через запятую)"));

        AddSection("Прочее",
            ("description", "Описание"),
            ("info", "Примечание"));
    }

    private void AddSection(string title, params (string Attribute, string Label)[] fields)
    {
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            Margin = new Thickness(0, 14, 0, 6)
        });

        int rows = (fields.Length + 1) / 2;
        var grid = FormBuilder.CreateGrid(rows);
        for (int i = 0; i < fields.Length; i++)
        {
            string attribute = fields[i].Attribute;
            string value = GetValue(attribute);
            _original[attribute] = value;
            TextBox box = FormBuilder.AddField(grid, i / 2, i % 2, fields[i].Label, value,
                attribute == "manager" ? "DN вида CN=… или логин; пусто — очистить" : null);
            _boxes[attribute] = box;
        }

        FieldsPanel.Children.Add(grid);
    }

    private string GetValue(string attribute)
    {
        return attribute.ToLowerInvariant() switch
        {
            "displayname" => _user.DisplayName,
            "givenname" => _user.FirstName,
            "sn" => _user.LastName,
            "samaccountname" => _user.SamAccountName,
            "userprincipalname" => _user.UserPrincipalName,
            "mail" => _user.Mail,
            "title" => _user.Title,
            "department" => _user.Department,
            "company" => _user.Company,
            "division" => _user.Division,
            "manager" => _user.ManagerDn,
            "employeeid" => _user.EmployeeId,
            "employeetype" => _user.EmployeeType,
            "telephonenumber" => _user.Phone,
            "mobile" => _user.Mobile,
            "ipphone" => _user.IpPhone,
            "physicaldeliveryofficename" => _user.Office,
            "streetaddress" => _user.Street,
            "l" => _user.City,
            "st" => _user.State,
            "postalcode" => _user.PostalCode,
            "co" => _user.Country,
            "wwwhomepage" => _user.WebPage,
            "profilepath" => _user.ProfilePath,
            "homedirectory" => _user.HomeDirectory,
            "homedrive" => _user.HomeDrive,
            "scriptpath" => _user.ScriptPath,
            "userworkstations" => _user.LogonWorkstations,
            "description" => _user.Description,
            "info" => _user.Notes,
            _ => _user.AllAttributes.TryGetValue(attribute, out string? v) ? v : string.Empty
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var changed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _boxes)
        {
            string value = pair.Value.Text.Trim();
            if (!_original.TryGetValue(pair.Key, out string? before)) before = string.Empty;
            if (string.Equals(value, before, StringComparison.Ordinal))
            {
                continue;
            }

            changed[pair.Key] = value.Length == 0 ? null : value;
        }

        if (changed.Count == 0)
        {
            DialogResult = false;
            return;
        }

        if (changed.ContainsKey("sAMAccountName") && string.IsNullOrWhiteSpace(changed["sAMAccountName"]))
        {
            ErrorText.Text = "Логин не может быть пустым.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Attributes = changed;
        DialogResult = true;
    }
}
