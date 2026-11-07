using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AdManager.Common;
using AdManager.Models;
using AdManager.Services;

namespace AdManager.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _problemChecks = new();
    private readonly bool _forcedDemo;

    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        _settings = current;
        _forcedDemo = !current.UseDemo && !AdServiceFactory.CanUseLiveDirectory(current);

        var grid = FormBuilder.CreateGrid(4);
        _boxes["Domain"] = FormBuilder.AddField(grid, 0, 0, "Домен", current.Domain, "например corp.local");
        _boxes["Server"] = FormBuilder.AddField(grid, 0, 1, "Сервер / DC", current.Server, "например dc01.corp.local");
        _boxes["StaleDays"] = FormBuilder.AddField(grid, 1, 0, "Неактивен, дней", current.StaleDays.ToString(CultureInfo.InvariantCulture));
        _boxes["PasswordExpiringDays"] = FormBuilder.AddField(grid, 1, 1, "Пароль истекает, дней", current.PasswordExpiringDays.ToString(CultureInfo.InvariantCulture));
        _boxes["PageSize"] = FormBuilder.AddField(grid, 2, 0, "Размер страницы LDAP", current.PageSize.ToString(CultureInfo.InvariantCulture));
        _boxes["MaxObjects"] = FormBuilder.AddField(grid, 2, 1, "Максимум объектов", current.MaxObjects.ToString(CultureInfo.InvariantCulture));
        _boxes["PasswordLength"] = FormBuilder.AddField(grid, 3, 0, "Длина пароля", current.PasswordLength.ToString(CultureInfo.InvariantCulture));
        FieldsHost.Children.Add(grid);

        UseDemoCheck.IsChecked = current.UseDemo || _forcedDemo;
        UseDemoCheck.IsEnabled = !_forcedDemo;
        if (_forcedDemo)
        {
            UseDemoCheck.ToolTip = "Домен недоступен — приложение автоматически использует демо-режим.";
        }

        IncludeDisabledCheck.IsChecked = current.IncludeDisabled;
        RecursiveCheck.IsChecked = current.Recursive;
        GroupsBox.Text = string.Join(Environment.NewLine, current.PrivilegedGroups);
        BuildProblemChecks(current.EnabledProblems);
    }

    private void BuildProblemChecks(IEnumerable<string> enabled)
    {
        var set = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        foreach (string name in ProblemCatalog.UserProblems)
        {
            UserProblemsPanel.Children.Add(CreateProblemCheck(name, set));
        }

        foreach (string name in ProblemCatalog.GroupProblems)
        {
            GroupProblemsPanel.Children.Add(CreateProblemCheck(name, set));
        }
    }

    private CheckBox CreateProblemCheck(string name, ISet<string> enabled)
    {
        var checkBox = new CheckBox
        {
            Content = name,
            Tag = name,
            Width = 315,
            Margin = new Thickness(0, 0, 8, 6),
            IsChecked = enabled.Contains(name)
        };
        _problemChecks.Add(checkBox);
        return checkBox;
    }

    private void ProblemsAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var checkBox in _problemChecks)
        {
            checkBox.IsChecked = true;
        }
    }

    private void ProblemsNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var checkBox in _problemChecks)
        {
            checkBox.IsChecked = false;
        }
    }

    private int ReadInt(string key, int fallback, int min, int max, List<string> errors, string label)
    {
        string text = _boxes[key].Text.Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            errors.Add($"{label}: нужно целое число.");
            return fallback;
        }

        if (value < min || value > max)
        {
            errors.Add($"{label}: допустимо от {min} до {max}.");
            return fallback;
        }

        return value;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var updated = _settings.Clone();

        updated.Domain = _boxes["Domain"].Text.Trim();
        updated.Server = _boxes["Server"].Text.Trim();
        updated.StaleDays = ReadInt("StaleDays", updated.StaleDays, 1, 3650, errors, "Неактивен, дней");
        updated.PasswordExpiringDays = ReadInt("PasswordExpiringDays", updated.PasswordExpiringDays, 1, 365, errors, "Пароль истекает, дней");
        updated.PageSize = ReadInt("PageSize", updated.PageSize, 100, 5000, errors, "Размер страницы LDAP");
        updated.MaxObjects = ReadInt("MaxObjects", updated.MaxObjects, 100, 500000, errors, "Максимум объектов");
        updated.PasswordLength = ReadInt("PasswordLength", updated.PasswordLength, 8, 64, errors, "Длина пароля");
        updated.UseDemo = _forcedDemo ? _settings.UseDemo : UseDemoCheck.IsChecked == true;
        updated.IncludeDisabled = IncludeDisabledCheck.IsChecked == true;
        updated.Recursive = RecursiveCheck.IsChecked == true;
        updated.EnabledProblems = _problemChecks
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag!)
            .ToList();

        var groups = GroupsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim())
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count > 0)
        {
            updated.PrivilegedGroups = groups;
        }

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("  ", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = updated;
        DialogResult = true;
    }
}
