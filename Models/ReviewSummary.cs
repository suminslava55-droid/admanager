using AdManager.Common;

namespace AdManager.Models;

public sealed class ReviewSummary : ObservableObject
{
    public int TotalUsers { get; set; }
    public int EnabledUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int LockedUsers { get; set; }
    public int PrivilegedUsers { get; set; }
    public int StaleUsers { get; set; }
    public int NeverLoggedIn { get; set; }
    public int PasswordNeverExpires { get; set; }
    public int SpnUsers { get; set; }
    public int AsRepUsers { get; set; }
    public int DelegationUsers { get; set; }
    public int SidHistoryUsers { get; set; }
    public int AdminCountUsers { get; set; }
    public int DisabledWithAccess { get; set; }
    public int MissingManager { get; set; }
    public int PasswordExpiring { get; set; }
    public int AccountExpiring { get; set; }
    public int TotalGroups { get; set; }
    public int PrivilegedGroups { get; set; }
    public int EmptyGroups { get; set; }
    public int GroupsWithoutOwner { get; set; }
    public int CriticalIssues { get; set; }
    public int HighIssues { get; set; }
    public int MediumIssues { get; set; }
    public int LowIssues { get; set; }
    public int TotalIssues { get; set; }
}
