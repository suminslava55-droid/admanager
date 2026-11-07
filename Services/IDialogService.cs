using System.Windows;
using AdManager.Models;

namespace AdManager.Services;

public interface IDialogService
{
    void SetOwner(Window owner);
    bool Confirm(string title, string message);
    void Info(string title, string message);
    void Error(string title, string message);

    PasswordResetOptions? ResetPassword(AdUser user, int passwordLength);
    IReadOnlyList<AdGroup> PickGroups(IReadOnlyList<AdGroup> available, string title);
    IReadOnlyList<AdUser> PickUsers(IReadOnlyList<AdUser> available, string title);
    Dictionary<string, string?>? EditUser(AdUser user);
    AdOu? PickOu(IReadOnlyList<AdOu> ous, string title, string? currentDn);
    NewUserRequest? CreateUser(string parentDn, IReadOnlyList<AdOu> ous);
    NewUserRequest? CopyUser(AdUser source, string parentDn, IReadOnlyList<AdOu> ous, int groupCount);
    NewGroupRequest? CreateGroup(string parentDn, IReadOnlyList<AdOu> ous);
    ExpirationChoice PickExpiration(DateTime? current);
    AppSettings? EditSettings(AppSettings current);
    string? SaveFile(string title, string filter, string defaultName);
}
