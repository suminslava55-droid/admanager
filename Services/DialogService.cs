using System.Windows;
using AdManager.Dialogs;
using AdManager.Models;

namespace AdManager.Services;

public sealed class DialogService : IDialogService
{
    private Window? _owner;

    public void SetOwner(Window owner) => _owner = owner;

    private Window? Owner => _owner ?? Application.Current?.MainWindow;

    private bool ShowDialog(Window window)
    {
        Window? owner = Owner;
        if (owner is not null && !ReferenceEquals(owner, window) && owner.IsLoaded)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }

    public bool Confirm(string title, string message)
    {
        var window = new MessageWindow(title, message, confirm: true, isError: false);
        return ShowDialog(window);
    }

    public void Info(string title, string message)
    {
        var window = new MessageWindow(title, message, confirm: false, isError: false);
        ShowDialog(window);
    }

    public void Error(string title, string message)
    {
        var window = new MessageWindow(title, message, confirm: false, isError: true);
        ShowDialog(window);
    }

    public PasswordResetOptions? ResetPassword(AdUser user, int passwordLength)
    {
        var window = new PasswordResetWindow(user, passwordLength);
        if (!ShowDialog(window) || window.Options is null)
        {
            return null;
        }

        window.Options.Unlock = window.UnlockRequested;
        return window.Options;
    }

    public IReadOnlyList<AdGroup> PickGroups(IReadOnlyList<AdGroup> available, string title)
    {
        var items = available
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new PickerItem
            {
                Value = g,
                Title = g.Name,
                Subtitle = string.Join(" · ", new[]
                {
                    g.GroupTypeText,
                    g.MemberCountText + " уч.",
                    string.IsNullOrWhiteSpace(g.OuDisplay) ? string.Empty : g.OuDisplay
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
            })
            .ToList();

        var window = new SelectionWindow(title, "Отметьте группы. Привилегированные отмечены в описании.", items);
        return ShowDialog(window)
            ? window.SelectedValues.OfType<AdGroup>().ToList()
            : Array.Empty<AdGroup>();
    }

    public IReadOnlyList<AdUser> PickUsers(IReadOnlyList<AdUser> available, string title)
    {
        var items = available
            .OrderBy(u => u.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => new PickerItem
            {
                Value = u,
                Title = u.DisplayLabel,
                Subtitle = string.Join(" · ", new[]
                {
                    u.SamAccountName,
                    string.IsNullOrWhiteSpace(u.Department) ? string.Empty : u.Department,
                    u.StatusText,
                    u.IsPrivileged ? "привилегированная" : string.Empty
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
            })
            .ToList();

        var window = new SelectionWindow(title, "Отметьте учётные записи для добавления в группу.", items);
        return ShowDialog(window)
            ? window.SelectedValues.OfType<AdUser>().ToList()
            : Array.Empty<AdUser>();
    }

    public Dictionary<string, string?>? EditUser(AdUser user)
    {
        var window = new EditUserWindow(user);
        return ShowDialog(window) ? window.Attributes : null;
    }

    public AdOu? PickOu(IReadOnlyList<AdOu> ous, string title, string? currentDn)
    {
        var window = new OuPickerWindow(title, ous, currentDn);
        return ShowDialog(window) ? window.SelectedOu : null;
    }

    public NewUserRequest? CreateUser(string parentDn, IReadOnlyList<AdOu> ous)
    {
        var window = new CreateUserWindow(parentDn, ous);
        return ShowDialog(window) ? window.Request : null;
    }

    public NewUserRequest? CopyUser(AdUser source, string parentDn, IReadOnlyList<AdOu> ous, int groupCount)
    {
        var window = new CreateUserWindow(parentDn, ous, source, groupCount);
        return ShowDialog(window) ? window.Request : null;
    }

    public NewGroupRequest? CreateGroup(string parentDn, IReadOnlyList<AdOu> ous)
    {
        var window = new CreateGroupWindow(parentDn, ous);
        return ShowDialog(window) ? window.Request : null;
    }

    public ExpirationChoice PickExpiration(DateTime? current)
    {
        var window = new ExpirationWindow(current);
        if (!ShowDialog(window))
        {
            return new ExpirationChoice { Canceled = true };
        }

        return window.Choice;
    }

    public AppSettings? EditSettings(AppSettings current)
    {
        var window = new SettingsWindow(current);
        return ShowDialog(window) ? window.Result : null;
    }

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultName,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
