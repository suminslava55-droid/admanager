using AdManager.Common;
using AdManager.Models;

namespace AdManager.Services;

/// <summary>Считает признаки риска и формирует отчёт access review.</summary>
public sealed class AccessReviewService
{
    private readonly AppSettings _settings;

    public AccessReviewService(AppSettings settings)
    {
        _settings = settings;
    }

    public ReviewResult Build(
        IReadOnlyList<AdUser> users,
        IReadOnlyList<AdGroup> groups,
        IReadOnlyList<AdGroup>? referenceGroups = null)
    {
        var summary = new ReviewSummary();
        var issues = new List<ReviewIssue>();

        // Для анализа привилегий нужны все группы домена, даже если в списке
        // отображается только выбранное OU.
        IReadOnlyList<AdGroup> reference = referenceGroups ?? groups;
        var groupByDn = reference
            .Where(g => !string.IsNullOrWhiteSpace(g.DistinguishedName))
            .GroupBy(g => g.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var displayedByDn = groups
            .Where(g => !string.IsNullOrWhiteSpace(g.DistinguishedName))
            .GroupBy(g => g.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var userByDn = users
            .Where(u => !string.IsNullOrWhiteSpace(u.DistinguishedName))
            .GroupBy(u => u.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var privilegedNames = new HashSet<string>(_settings.PrivilegedGroups, StringComparer.OrdinalIgnoreCase);
        var privilegedGroupDns = reference
            .Where(g => IsPrivilegedGroup(g, privilegedNames))
            .Select(g => g.DistinguishedName)
            .Where(dn => !string.IsNullOrWhiteSpace(dn))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            group.IsPrivileged = privilegedGroupDns.Contains(group.DistinguishedName);
            group.IsNestedPrivileged = false;
        }

        var privilegedUsers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in reference.Where(g => privilegedGroupDns.Contains(g.DistinguishedName)))
        {
            ExpandPrivileged(group, groupByDn, userByDn, displayedByDn, privilegedUsers,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        DateTime now = DateTime.UtcNow;

        foreach (var user in users)
        {
            user.IsPrivileged = privilegedUsers.TryGetValue(user.DistinguishedName, out var privilegedVia);
            user.PrivilegedGroups = privilegedVia ?? new List<string>();

            DateTime? lastLogon = Max(user.LastLogon, user.LastLogonTimestamp);
            user.IsNeverLoggedIn = lastLogon is null && user.LogonCount == 0;
            user.DaysInactive = lastLogon.HasValue ? (int)(now - lastLogon.Value).TotalDays : -1;
            user.IsStale = user.IsNeverLoggedIn ||
                           (lastLogon.HasValue && (now - lastLogon.Value).TotalDays > _settings.StaleDays);

            user.PasswordAgeDays = user.PasswordLastSet.HasValue
                ? (int)(now - user.PasswordLastSet.Value).TotalDays
                : -1;
            user.IsPasswordExpired = !user.PasswordNeverExpires &&
                                     user.PasswordExpires.HasValue &&
                                     user.PasswordExpires.Value < now;
            user.IsAccountExpired = user.AccountExpires.HasValue && user.AccountExpires.Value < now;
            user.DaysToExpiry = user.AccountExpires.HasValue
                ? (int)(user.AccountExpires.Value - now).TotalDays
                : 0;
            user.IsDisabledWithAccess = !user.Enabled && user.MemberOf.Count > 0;

            user.RiskFlags.Clear();
            user.Issues.Clear();

            if (user.IsPrivileged)
            {
                AddFlag(user, "Привилегированная УЗ", user.Enabled ? RiskSeverity.High : RiskSeverity.Medium,
                    "Состоит в: " + string.Join(", ", user.PrivilegedGroups.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)));
            }

            if (user.IsPrivileged && user.IsStale)
            {
                AddFlag(user, "Привилегированная и неактивная",
                    user.Enabled ? RiskSeverity.Critical : RiskSeverity.Medium,
                    user.IsNeverLoggedIn
                        ? "Привилегированная УЗ никогда не входила."
                        : $"Привилегированная УЗ не входила {user.DaysInactive} дн.");
            }

            if (user.AdminCount)
            {
                AddFlag(user, "adminCount=1", RiskSeverity.Medium,
                    "Объект защищён AdminSDHolder; массовые изменения могут быть перезаписаны.");
            }

            if (user.HasSpn)
            {
                AddFlag(user, "SPN (Kerberoastable)", RiskSeverity.High,
                    "servicePrincipalName: " + string.Join(", ", user.Spn));
            }

            if (user.DontRequirePreAuth)
            {
                AddFlag(user, "AS-REP (без preauth)", RiskSeverity.High,
                    "Включён DONT_REQUIRE_PREAUTH — возможен AS-REP roasting.");
            }

            if (user.TrustedForDelegation)
            {
                AddFlag(user, "Unconstrained delegation", RiskSeverity.Critical,
                    "Включён TRUSTED_FOR_DELEGATION.");
            }

            if (user.HasConstrainedDelegation)
            {
                AddFlag(user, "Constrained delegation", RiskSeverity.Medium,
                    "msDS-AllowedToDelegateTo: " + string.Join(", ", user.AllowedToDelegateTo));
            }

            if (user.TrustedToAuthForDelegation)
            {
                AddFlag(user, "Protocol transition", RiskSeverity.Medium,
                    "Включён TRUSTED_TO_AUTH_FOR_DELEGATION.");
            }

            if (user.HasSidHistory)
            {
                AddFlag(user, "SID History", RiskSeverity.High,
                    "sIDHistory: " + string.Join(", ", user.SidHistory));
            }

            if (user.PasswordNeverExpires)
            {
                AddFlag(user, "Пароль не истекает", RiskSeverity.Medium,
                    "Включён DONT_EXPIRE_PASSWORD.");
            }

            if (user.PasswordNotRequired)
            {
                AddFlag(user, "Пароль не требуется", RiskSeverity.High,
                    "Включён PASSWD_NOTREQD.");
            }

            if (user.UseDesKeyOnly)
            {
                AddFlag(user, "DES only", RiskSeverity.High,
                    "Включён USE_DES_KEY_ONLY — слабое шифрование Kerberos.");
            }

            if (user.NoAuthDataRequired)
            {
                AddFlag(user, "No auth data required", RiskSeverity.Medium,
                    "Включён NO_AUTH_DATA_REQUIRED.");
            }

            if (user.IsStale && !user.IsPrivileged && user.Enabled)
            {
                AddFlag(user, "Неактивная УЗ", RiskSeverity.Medium,
                    user.IsNeverLoggedIn
                        ? "Никогда не входила."
                        : $"Не входила {user.DaysInactive} дн. (порог {_settings.StaleDays}).");
            }

            if (user.IsDisabledWithAccess)
            {
                // Отключённая УЗ не может аутентифицироваться, поэтому это не
                // текущая возможность входа, а забытое членство/неполная блокировка.
                // Для привилегированных групп — выше, так как при включении УЗ
                // сразу вернёт повышенные права.
                RiskSeverity severity = user.IsPrivileged ? RiskSeverity.Medium : RiskSeverity.Low;
                AddFlag(user, "Отключена, но в группах", severity,
                    user.IsPrivileged
                        ? $"Отключена, но сохраняет членство в {user.MemberOf.Count} группах, включая привилегированные. При повторном включении вернёт повышенные права."
                        : $"Отключена, но сохраняет членство в {user.MemberOf.Count} группах. Вход невозможен; членство стоит очистить при увольнении.");
            }

            if (user.IsAccountExpired)
            {
                AddFlag(user, "Срок действия истёк", RiskSeverity.Medium,
                    "accountExpires: " + user.AccountExpiresText);
            }

            if (user.IsPasswordExpired)
            {
                AddFlag(user, "Пароль истёк", RiskSeverity.Medium,
                    "Срок действия пароля истёк.");
            }
            else if (user.MustChangePassword)
            {
                AddFlag(user, "Требуется смена пароля", RiskSeverity.Medium,
                    "pwdLastSet=0 — пароль должен быть сменён при следующем входе.");
            }
            else if (!user.PasswordNeverExpires &&
                     user.PasswordExpires.HasValue &&
                     user.PasswordExpires.Value > now &&
                     (user.PasswordExpires.Value - now).TotalDays <= _settings.PasswordExpiringDays)
            {
                AddFlag(user, "Пароль скоро истекает", RiskSeverity.Low,
                    "Осталось " + Math.Max(0, (int)(user.PasswordExpires.Value - now).TotalDays) + " дн.");
            }

            ApplyProblemFilter(user);
            user.RefreshDerived();
        }

        foreach (var group in groups)
        {
            group.IsEmpty = group.MemberDns.Count == 0;
            group.HasNoOwner = string.IsNullOrWhiteSpace(group.ManagedByDn);
            group.RiskFlags.Clear();
            group.Issues.Clear();

            if (group.IsPrivileged)
            {
                AddGroupFlag(group, "Привилегированная группа", RiskSeverity.High,
                    "Группа входит в список привилегированных.");
            }

            if (group.IsNestedPrivileged)
            {
                AddGroupFlag(group, "Вложена в привилегированную", RiskSeverity.Medium,
                    "Членство в этой группе косвенно даёт привилегии.");
            }

            if (group.IsPrivileged && group.HasNoOwner)
            {
                AddGroupFlag(group, "Привилегированная без владельца", RiskSeverity.Medium,
                    "У привилегированной группы не заполнен managedBy. На доступ это не влияет, но без ответственного сложнее контролировать членство.");
            }

            if (group.IsEmpty)
            {
                AddGroupFlag(group, "Пустая группа", RiskSeverity.Low, "В группе нет участников.");
            }

            if (group.HasNoOwner && !group.IsPrivileged)
            {
                AddGroupFlag(group, "Без владельца", RiskSeverity.Info,
                    "Атрибут managedBy не заполнен. На вход и права это не влияет; ответственный нужен для контроля членства и продления доступов.");
            }

            ApplyProblemFilter(group);
            group.RefreshDerived();
        }

        issues = users.SelectMany(u => u.Issues)
            .Concat(groups.SelectMany(g => g.Issues))
            .ToList();

        summary.TotalUsers = users.Count;
        summary.EnabledUsers = users.Count(u => u.Enabled);
        summary.DisabledUsers = users.Count(u => !u.Enabled);
        summary.LockedUsers = users.Count(u => u.Locked);
        summary.PrivilegedUsers = users.Count(u => u.IsPrivileged);
        summary.StaleUsers = users.Count(u => u.IsStale);
        summary.NeverLoggedIn = users.Count(u => u.IsNeverLoggedIn);
        summary.PasswordNeverExpires = users.Count(u => u.PasswordNeverExpires);
        summary.SpnUsers = users.Count(u => u.HasSpn);
        summary.AsRepUsers = users.Count(u => u.DontRequirePreAuth);
        summary.DelegationUsers = users.Count(u => u.TrustedForDelegation || u.TrustedToAuthForDelegation || u.HasConstrainedDelegation);
        summary.SidHistoryUsers = users.Count(u => u.HasSidHistory);
        summary.AdminCountUsers = users.Count(u => u.AdminCount);
        summary.DisabledWithAccess = users.Count(u => u.IsDisabledWithAccess);
        summary.MissingManager = users.Count(u => u.HasNoManager);
        summary.PasswordExpiring = users.Count(u => u.PasswordExpires.HasValue && u.PasswordExpires.Value > now &&
                                                     (u.PasswordExpires.Value - now).TotalDays <= _settings.PasswordExpiringDays);
        summary.AccountExpiring = users.Count(u => u.AccountExpires.HasValue && u.AccountExpires.Value > now &&
                                                   (u.AccountExpires.Value - now).TotalDays <= 30);
        summary.TotalGroups = groups.Count;
        summary.PrivilegedGroups = groups.Count(g => g.IsPrivileged);
        summary.EmptyGroups = groups.Count(g => g.IsEmpty);
        summary.GroupsWithoutOwner = groups.Count(g => g.HasNoOwner);
        summary.CriticalIssues = issues.Count(i => i.Severity == RiskSeverity.Critical);
        summary.HighIssues = issues.Count(i => i.Severity == RiskSeverity.High);
        summary.MediumIssues = issues.Count(i => i.Severity == RiskSeverity.Medium);
        summary.LowIssues = issues.Count(i => i.Severity == RiskSeverity.Low);
        summary.TotalIssues = issues.Count;
        summary.RaiseAll();

        return new ReviewResult
        {
            Summary = summary,
            Issues = issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.ObjectName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.Category, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    private static void ExpandPrivileged(
        AdGroup group,
        IReadOnlyDictionary<string, AdGroup> groups,
        IReadOnlyDictionary<string, AdUser> users,
        IReadOnlyDictionary<string, AdGroup> displayedGroups,
        IDictionary<string, List<string>> privilegedUsers,
        ISet<string> visited)
    {
        if (!visited.Add(group.DistinguishedName)) return;

        foreach (string memberDn in group.MemberDns)
        {
            if (users.TryGetValue(memberDn, out AdUser? user))
            {
                if (!privilegedUsers.TryGetValue(user.DistinguishedName, out var via))
                {
                    via = new List<string>();
                    privilegedUsers[user.DistinguishedName] = via;
                }

                if (!via.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
                {
                    via.Add(group.Name);
                }
            }
            else if (groups.TryGetValue(memberDn, out AdGroup? child))
            {
                if (displayedGroups.TryGetValue(child.DistinguishedName, out AdGroup? shown))
                {
                    shown.IsNestedPrivileged = true;
                }

                ExpandPrivileged(child, groups, users, displayedGroups, privilegedUsers, visited);
            }
        }
    }

    private static readonly HashSet<string> PrivilegedBuiltinSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-32-544", // Administrators
        "S-1-5-32-548", // Account Operators
        "S-1-5-32-549", // Server Operators
        "S-1-5-32-550", // Print Operators
        "S-1-5-32-551", // Backup Operators
        "S-1-5-32-552"  // Replicator
    };

    private static readonly HashSet<int> PrivilegedDomainRids = new()
    {
        512, // Domain Admins
        518, // Schema Admins
        519, // Enterprise Admins
        520, // Group Policy Creator Owners
        525, // Protected Users
        526, // Key Admins
        527  // Enterprise Key Admins
    };

    /// <summary>
    /// Привилегированность определяется по настраиваемому списку имён, а также
    /// по well-known SID — это важно для локализованных групп вроде «Администраторы»
    /// (S-1-5-32-544), у которых sAMAccountName тоже локализован.
    /// </summary>
    private static bool IsPrivilegedGroup(AdGroup group, ISet<string> configuredNames)
    {
        if (configuredNames.Contains(group.Name) ||
            configuredNames.Contains(group.SamAccountName) ||
            configuredNames.Contains(group.ObjectSid))
        {
            return true;
        }

        return IsWellKnownPrivilegedSid(group.ObjectSid);
    }

    private static bool IsWellKnownPrivilegedSid(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }

        if (PrivilegedBuiltinSids.Contains(sid))
        {
            return true;
        }

        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int dash = sid.LastIndexOf('-');
        return dash > 0 &&
               int.TryParse(sid[(dash + 1)..], out int rid) &&
               PrivilegedDomainRids.Contains(rid);
    }

    private static DateTime? Max(DateTime? a, DateTime? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return a.Value >= b.Value ? a : b;
    }

    private bool IsProblemEnabled(string name)
    {
        var enabled = _settings.EnabledProblems;
        if (enabled is null) return true;
        return enabled.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyProblemFilter(AdUser user)
    {
        user.RiskFlags.RemoveAll(flag => !IsProblemEnabled(flag.Name));
        user.Issues.RemoveAll(issue => !IsProblemEnabled(issue.Category));
    }

    private void ApplyProblemFilter(AdGroup group)
    {
        group.RiskFlags.RemoveAll(flag => !IsProblemEnabled(flag.Name));
        group.Issues.RemoveAll(issue => !IsProblemEnabled(issue.Category));
    }

    private static void AddFlag(AdUser user, string name, RiskSeverity severity, string details)
    {
        if (user.RiskFlags.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        user.RiskFlags.Add(new RiskFlag { Name = name, Severity = severity, Details = details });
        user.Issues.Add(new ReviewIssue
        {
            Severity = severity,
            Category = name,
            ObjectName = user.DisplayLabel,
            SamAccountName = user.SamAccountName,
            Details = details,
            ObjectDn = user.DistinguishedName,
            User = user
        });
    }

    private static void AddGroupFlag(AdGroup group, string name, RiskSeverity severity, string details)
    {
        if (group.RiskFlags.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        group.RiskFlags.Add(new RiskFlag { Name = name, Severity = severity, Details = details });
        group.Issues.Add(new ReviewIssue
        {
            Severity = severity,
            Category = name,
            ObjectName = group.Name,
            SamAccountName = group.SamAccountName,
            Details = details,
            ObjectDn = group.DistinguishedName,
            Group = group
        });
    }
}
