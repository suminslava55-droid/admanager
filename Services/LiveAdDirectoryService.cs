using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using AdManager.Common;
using AdManager.Models;

namespace AdManager.Services;

/// <summary>Работа с настоящим доменом через System.DirectoryServices (LDAP).</summary>
public sealed class LiveAdDirectoryService : IAdDirectoryService
{
    private static readonly string[] UserProperties =
    {
        "distinguishedName", "cn", "name", "displayName", "givenName", "sn", "initials",
        "sAMAccountName", "userPrincipalName", "mail", "title", "department", "company",
        "division", "manager", "telephoneNumber", "mobile", "ipPhone", "homePhone",
        "facsimileTelephoneNumber", "physicalDeliveryOfficeName", "streetAddress", "l", "st",
        "postalCode", "co", "wWWHomePage", "description", "info", "notes", "employeeID",
        "employeeType", "objectSid", "objectGUID", "whenCreated", "whenChanged",
        "userAccountControl", "lockoutTime", "pwdLastSet", "msDS-UserPasswordExpiryTimeComputed",
        "accountExpires", "lastLogon", "lastLogonTimestamp", "logonCount", "badPwdCount",
        "lastBadPasswordAttempt", "adminCount", "servicePrincipalName", "msDS-AllowedToDelegateTo",
        "sIDHistory", "primaryGroupID", "memberOf", "profilePath", "homeDirectory", "homeDrive",
        "scriptPath", "userWorkstations", "logonHours", "mailNickname", "proxyAddresses",
        "targetAddress", "msExchHideFromAddressLists"
    };

    private static readonly string[] GroupProperties =
    {
        "distinguishedName", "cn", "name", "sAMAccountName", "description", "info", "notes",
        "groupType", "member", "memberOf", "managedBy", "objectSid", "objectGUID",
        "whenCreated", "whenChanged"
    };

    private readonly int _pageSize;
    private DirectoryEntry? _root;
    private string _domain = string.Empty;
    private string _server = string.Empty;
    private string _rootDn = string.Empty;
    private string _currentUser = string.Empty;
    private string _error = string.Empty;
    private string _domainSid = string.Empty;
    private string _eventLogServer = string.Empty;
    private readonly Dictionary<string, (string Value, DateTime CachedAt)> _lastLogonCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = new();

    private void Warn(string message)
    {
        lock (_warnings)
        {
            if (_warnings.Count < 500)
            {
                _warnings.Add($"{DateTime.Now:HH:mm:ss} {message}");
            }
        }
    }

    public LiveAdDirectoryService(int pageSize = 500)
    {
        _pageSize = Math.Clamp(pageSize, 100, 5000);
    }

    public bool IsConnected { get; private set; }
    public bool IsDemo => false;
    public string DomainName => _domain;
    public string ServerName => _server;
    public string RootDn => _rootDn;
    public string CurrentUser => _currentUser;
    public string ConnectionError => _error;

    public Task ConnectAsync(string? domain, string? server, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                IsConnected = false;
                _error = string.Empty;
                _currentUser = WindowsIdentity.GetCurrent().Name;

                string domainName = (domain ?? string.Empty).Trim();
                string serverName = (server ?? string.Empty).Trim();

                if (domainName.Length == 0)
                {
                    var current = Domain.GetCurrentDomain();
                    domainName = current.Name;
                }

                if (serverName.Length == 0)
                {
                    serverName = domainName;
                }

                var entry = new DirectoryEntry(
                    "LDAP://" + serverName,
                    null,
                    null,
                    AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing);

                entry.RefreshCache();
                string dn = AdValue.GetString(entry.Properties["distinguishedName"].Value);

                if (string.IsNullOrEmpty(dn))
                {
                    using var rootDse = new DirectoryEntry(
                        "LDAP://" + serverName + "/RootDSE",
                        null,
                        null,
                        AuthenticationTypes.Secure);
                    dn = AdValue.GetString(rootDse.Properties["defaultNamingContext"].Value);
                }

                if (string.IsNullOrEmpty(dn))
                {
                    entry.Dispose();
                    throw new InvalidOperationException("Не удалось получить корневой DN домена.");
                }

                _root?.Dispose();
                _root = entry;
                _domain = domainName;
                _server = serverName;
                _rootDn = dn;
                try
                {
                    _eventLogServer = Domain.GetCurrentDomain().DomainControllers
                        .Cast<DomainController>()
                        .Select(dc => dc.Name)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? serverName;
                }
                catch
                {
                    _eventLogServer = serverName;
                }
                try
                {
                    entry.RefreshCache(new[] { "objectSid" });
                    if (entry.Properties["objectSid"].Value is byte[] domainSid)
                    {
                        _domainSid = new SecurityIdentifier(domainSid, 0).Value;
                    }
                }
                catch (Exception ex)
                {
                    Warn("Не удалось прочитать SID домена: " + ex.Message);
                }

                _warnings.Clear();
                IsConnected = true;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                IsConnected = false;
            }
        }, ct);
    }

    // ------------------------------------------------------------------
    //  Чтение
    // ------------------------------------------------------------------

    public Task<List<AdOu>> LoadOusAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var result = new List<AdOu>();
            if (!IsConnected)
            {
                return result;
            }

            using var searchRoot = OpenEntry(_rootDn);
            using var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = "(|(objectClass=organizationalUnit)(objectClass=container)(objectClass=builtinDomain))",
                SearchScope = SearchScope.Subtree,
                PageSize = _pageSize,
                SizeLimit = 20000,
                ServerTimeLimit = TimeSpan.FromSeconds(60),
                ClientTimeout = TimeSpan.FromSeconds(60)
            };

            searcher.PropertiesToLoad.AddRange(new[]
            {
                "ou", "cn", "name", "distinguishedName", "description"
            });

            foreach (SearchResult res in searcher.FindAll())
            {
                ct.ThrowIfCancellationRequested();
                string dn = Str(res, "distinguishedName");
                if (string.IsNullOrWhiteSpace(dn) || dn.Equals(_rootDn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Str(res, "ou");
                if (string.IsNullOrWhiteSpace(name)) name = Str(res, "cn");
                if (string.IsNullOrWhiteSpace(name)) name = Str(res, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new AdOu
                {
                    Name = name,
                    DistinguishedName = dn,
                    ParentDn = AdValue.DnParent(dn),
                    Description = Str(res, "description")
                });
            }

            return result
                .GroupBy(o => o.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(o => o.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);
    }

    public Task<List<AdUser>> LoadUsersAsync(
        string? baseDn,
        bool recursive,
        string? search,
        bool includeDisabled,
        int max,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var result = new List<AdUser>();
            if (!IsConnected)
            {
                return result;
            }

            using var searchRoot = OpenEntry(string.IsNullOrWhiteSpace(baseDn) ? _rootDn : baseDn);
            using var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = BuildUserFilter(search, includeDisabled),
                SearchScope = recursive ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = _pageSize,
                SizeLimit = Math.Max(1, max),
                ServerTimeLimit = TimeSpan.FromSeconds(90),
                ClientTimeout = TimeSpan.FromSeconds(90)
            };

            searcher.PropertiesToLoad.AddRange(UserProperties);

            foreach (SearchResult res in searcher.FindAll())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    result.Add(MapUser(res));
                }
                catch (Exception ex)
                {
                    // Один проблемный объект не должен ронять весь список.
                    Warn($"Пользователь не прочитан: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return result
                .OrderBy(u => u.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, ct);
    }

    public Task<List<AdGroup>> LoadGroupsAsync(
        string? baseDn,
        bool recursive,
        string? search,
        int max,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var result = new List<AdGroup>();
            if (!IsConnected)
            {
                return result;
            }

            using var searchRoot = OpenEntry(string.IsNullOrWhiteSpace(baseDn) ? _rootDn : baseDn);
            using var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = BuildGroupFilter(search),
                SearchScope = recursive ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = _pageSize,
                SizeLimit = Math.Max(1, max),
                ServerTimeLimit = TimeSpan.FromSeconds(90),
                ClientTimeout = TimeSpan.FromSeconds(90)
            };

            searcher.PropertiesToLoad.AddRange(GroupProperties);

            foreach (SearchResult res in searcher.FindAll())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    result.Add(MapGroup(res));
                }
                catch (Exception ex)
                {
                    // Пропускаем проблемную группу.
                    Warn($"Группа не прочитана: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return result
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, ct);
    }

    public Task<List<AdGroup>> SearchGroupsAsync(string? search, int max, CancellationToken ct)
        => LoadGroupsAsync(_rootDn, true, search, max, ct);

    public Task LoadUserDetailsAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.RefreshCache();

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string name in entry.Properties.PropertyNames.Cast<string>().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    var values = new List<string>();
                    foreach (object? value in entry.Properties[name])
                    {
                        try
                        {
                            values.Add(AdValue.FormatAttributeValue(name, value));
                        }
                        catch
                        {
                            // Пропускаем значение, которое не удалось отформатировать.
                        }
                    }

                    if (values.Count > 0)
                    {
                        dict[name] = string.Join("; ", values);
                    }
                }
            }
            catch (Exception ex)
            {
                Warn($"LoadUserDetails attributes({user.DistinguishedName}): {ex.GetType().Name}: {ex.Message}");
            }

            user.AllAttributes = dict;
            user.AllAttributeItems = dict
                .Select(kv => new AttributeItem(kv.Key, kv.Value))
                .ToList();

            // Перечитываем ключевые признаки из AD, чтобы карточка не показывала
            // устаревший статус из первоначального списка.
            bool hasUac = First(entry, "userAccountControl") is not null;
            bool hasPwdLastSet = First(entry, "pwdLastSet") is not null;
            int rawUac = AdValue.GetInt(First(entry, "userAccountControl"));
            long lockoutTime = AdValue.GetLong(First(entry, "lockoutTime"));
            long pwdLastSet = AdValue.GetLong(First(entry, "pwdLastSet"));
            long passwordExpiry = AdValue.GetLong(First(entry, "msDS-UserPasswordExpiryTimeComputed"));

            // Часть атрибутов может не отдаваться по LDAP (например, если в схеме
            // они помечены как confidential). В этом случае статус показываем
            // как «н/д», но не выполняем S4U-вход от имени пользователя.
            user.UserAccountControl = rawUac;
            user.AccountStateReliable = hasUac;
            user.Enabled = hasUac ? !AdValue.HasFlag(rawUac, 0x2) : true;
            user.Locked = hasUac && lockoutTime > 0;
            user.PasswordNeverExpires = hasUac && AdValue.HasFlag(rawUac, 0x10000);
            user.PasswordNotRequired = hasUac && AdValue.HasFlag(rawUac, 0x20);
            user.MustChangePassword = hasPwdLastSet && pwdLastSet == 0;
            user.PasswordLastSet = hasPwdLastSet && pwdLastSet > 0
                ? AdValue.FileTimeToDateTime(pwdLastSet)
                : null;
            user.PasswordExpires = user.PasswordNeverExpires
                ? null
                : AdValue.FileTimeToDateTime(passwordExpiry);
            user.AccountExpires = AdValue.AccountExpiresToDateTime(
                AdValue.GetLong(First(entry, "accountExpires")));
            user.IsPasswordExpired = !user.PasswordNeverExpires &&
                                     user.PasswordExpires.HasValue &&
                                     user.PasswordExpires.Value < DateTime.UtcNow;
            user.IsAccountExpired = user.AccountExpires.HasValue &&
                                    user.AccountExpires.Value < DateTime.UtcNow;

            // memberOf не всегда попадает в первоначальную выборку (например, из-за
            // кэша/прав), поэтому перечитываем его при открытии карточки.
            user.MemberOf = ReadStrings(entry, "memberOf");

            user.PrimaryGroupId = AdValue.GetInt(First(entry, "primaryGroupID"));
            string? primaryGroupDn = ResolvePrimaryGroupDn(user.PrimaryGroupId);
            user.PrimaryGroupDn = primaryGroupDn ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(primaryGroupDn) &&
                !user.MemberOf.Contains(primaryGroupDn, StringComparer.OrdinalIgnoreCase))
            {
                user.MemberOf.Add(primaryGroupDn);
            }

            user.DetailsLoaded = true;
            try
            {
                user.LastLogonWorkstation = FindLastLogonWorkstation(user.SamAccountName, ct) ?? string.Empty;
            }
            catch (Exception ex)
            {
                Warn("LastLogonWorkstation: " + ex.GetType().Name + ": " + ex.Message);
                user.LastLogonWorkstation = string.Empty;
            }

            user.RaiseAll();
            }
            catch (Exception ex)
            {
                Warn($"LoadUserDetails({user.DistinguishedName}): {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }, ct);
    }

    public Task LoadGroupDetailsAsync(AdGroup group, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(group.DistinguishedName);
            entry.RefreshCache();

            var memberDns = ReadMemberDns(entry);
            if (memberDns.Count > 0 || group.MemberDns.Count == 0)
            {
                group.MemberDns = memberDns;
            }
            group.MemberOf = ReadStrings(entry, "memberOf");
            group.GroupTypeRaw = AdValue.GetInt(First(entry, "groupType"));
            group.GroupTypeText = AdValue.GroupTypeText(group.GroupTypeRaw);
            group.GroupScopeText = AdValue.GroupScopeText(group.GroupTypeRaw);
            group.IsSecurity = AdValue.IsSecurityGroup(group.GroupTypeRaw);
            group.ManagedByDn = AdValue.GetString(First(entry, "managedBy"));
            group.ManagedBy = AdValue.DnToName(group.ManagedByDn);
            group.IsEmpty = group.MemberDns.Count == 0;
            group.HasNoOwner = string.IsNullOrWhiteSpace(group.ManagedByDn);

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in entry.Properties.PropertyNames.Cast<string>().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var values = new List<string>();
                foreach (object? value in entry.Properties[name])
                {
                    values.Add(AdValue.FormatAttributeValue(name, value));
                }

                if (values.Count > 0) dict[name] = string.Join("; ", values);
            }

            group.AllAttributes = dict;
            group.AllAttributeItems = dict.Select(kv => new AttributeItem(kv.Key, kv.Value)).ToList();
            group.DetailsLoaded = true;
            group.RefreshDerived();
        }, ct);
    }

    public async Task<List<AdMember>> LoadGroupMembersAsync(AdGroup group, CancellationToken ct)
    {
        if (group.MemberDns.Count == 0 && !group.DetailsLoaded)
        {
            await LoadGroupDetailsAsync(group, ct);
        }

        var members = ResolveDns(group.MemberDns, ct);
        group.Members = members;
        group.RaiseAll();
        return members;
    }

    public Task<List<string>> LoadUserEffectiveGroupDnsAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                // tokenGroups — вычисляемый атрибут LDAP. В отличие от
                // AccountManagement.GetAuthorizationGroups он не выполняет
                // S4U-вход от имени просматриваемого пользователя и не создаёт
                // событий 4648 в журнале Security.
                var sids = ReadTokenGroupSids(user.DistinguishedName);
                return ResolveGroupDnsBySids(sids, ct)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Warn("tokenGroups: " + ex.GetType().Name + ": " + ex.Message);
                return new List<string>();
            }
        }, ct);
    }

    // ------------------------------------------------------------------
    //  Запись
    // ------------------------------------------------------------------

    public Task SetUserEnabledAsync(AdUser user, bool enabled, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            int uac = AdValue.GetInt(entry.Properties["userAccountControl"].Value, 512);
            uac = enabled ? uac & ~0x2 : uac | 0x2;
            entry.Properties["userAccountControl"].Value = uac;
            entry.CommitChanges();

            user.UserAccountControl = uac;
            user.Enabled = enabled;
            user.RefreshDerived();
        }, ct);
    }

    public Task UnlockUserAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.Properties["lockoutTime"].Value = 0;
            entry.CommitChanges();

            user.Locked = false;
            user.LockoutTime = null;
            user.RefreshDerived();
        }, ct);
    }

    public Task ResetPasswordAsync(AdUser user, string password, bool mustChange, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.Invoke("SetPassword", new object[] { password });

            if (mustChange)
            {
                entry.Properties["pwdLastSet"].Value = 0;
                entry.CommitChanges();
            }

            user.PasswordLastSet = DateTime.UtcNow;
            user.MustChangePassword = mustChange;
            user.IsPasswordExpired = false;
            user.RefreshDerived();
        }, ct);
    }

    public Task SetPasswordNeverExpiresAsync(AdUser user, bool value, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            int uac = AdValue.GetInt(entry.Properties["userAccountControl"].Value, 512);
            uac = value ? uac | 0x10000 : uac & ~0x10000;
            entry.Properties["userAccountControl"].Value = uac;
            entry.CommitChanges();

            user.UserAccountControl = uac;
            user.PasswordNeverExpires = value;
            user.PasswordExpires = value ? null : user.PasswordExpires;
            user.RefreshDerived();
        }, ct);
    }

    public Task SetMustChangePasswordAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.Properties["pwdLastSet"].Value = 0;
            entry.CommitChanges();

            user.MustChangePassword = true;
            user.RefreshDerived();
        }, ct);
    }

    public Task ResetPasswordAgeAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            // -1 — специальное значение AD: «пароль изменён сейчас».
            // Сам пароль не меняется, но срок его действия начинается заново.
            entry.Properties["pwdLastSet"].Value = -1;
            entry.CommitChanges();

            user.PasswordLastSet = DateTime.UtcNow;
            user.MustChangePassword = false;
            user.IsPasswordExpired = false;
            user.RefreshDerived();
        }, ct);
    }

    public Task SetAccountExpirationAsync(AdUser user, DateTime? expiration, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.Properties["accountExpires"].Value = AdValue.DateTimeToAccountExpires(expiration);
            entry.CommitChanges();

            user.AccountExpires = expiration;
            user.IsAccountExpired = expiration.HasValue && expiration.Value.ToUniversalTime() < DateTime.UtcNow;
            user.RefreshDerived();
        }, ct);
    }

    public Task MoveObjectAsync(string dn, string newParentDn, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(dn);
            using var parent = OpenEntry(newParentDn);
            entry.MoveTo(parent);
        }, ct);
    }

    public Task AddToGroupAsync(string groupDn, string memberDn, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var group = OpenEntry(groupDn);
            var members = group.Properties["member"];
            if (!members.Contains(memberDn))
            {
                members.Add(memberDn);
                group.CommitChanges();
            }
        }, ct);
    }

    public Task RemoveFromGroupAsync(string groupDn, string memberDn, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var group = OpenEntry(groupDn);
            var members = group.Properties["member"];
            if (members.Contains(memberDn))
            {
                members.Remove(memberDn);
                group.CommitChanges();
            }
        }, ct);
    }

    public Task UpdateUserAttributesAsync(AdUser user, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            foreach (var pair in attributes)
            {
                ct.ThrowIfCancellationRequested();
                string name = pair.Key;
                string? value = pair.Value;

                if (name.Equals("manager", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value)
                    && !value.Contains('='))
                {
                    value = ResolveUserDn(value) ?? value;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    entry.Properties[name].Clear();
                }
                else
                {
                    entry.Properties[name].Value = value;
                }
            }

            entry.CommitChanges();
        }, ct);
    }

    public Task<string> CreateUserAsync(NewUserRequest request, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var parent = OpenEntry(request.ParentDn);
            string cn = string.IsNullOrWhiteSpace(request.Cn) ? request.DisplayName : request.Cn;
            if (string.IsNullOrWhiteSpace(cn))
            {
                throw new InvalidOperationException("Не задано имя пользователя (CN).");
            }

            string newDn = "CN=" + AdValue.EscapeDnValue(cn) + "," + request.ParentDn;
            using var user = parent.Children.Add("CN=" + AdValue.EscapeDnValue(cn), "user");
            user.Properties["sAMAccountName"].Value = request.SamAccountName;
            if (!string.IsNullOrWhiteSpace(request.UserPrincipalName))
            {
                user.Properties["userPrincipalName"].Value = request.UserPrincipalName;
            }

            user.Properties["displayName"].Value = request.DisplayName;
            if (!string.IsNullOrWhiteSpace(request.FirstName)) user.Properties["givenName"].Value = request.FirstName;
            if (!string.IsNullOrWhiteSpace(request.LastName)) user.Properties["sn"].Value = request.LastName;
            if (!string.IsNullOrWhiteSpace(request.Mail)) user.Properties["mail"].Value = request.Mail;
            if (!string.IsNullOrWhiteSpace(request.Title)) user.Properties["title"].Value = request.Title;
            if (!string.IsNullOrWhiteSpace(request.Department)) user.Properties["department"].Value = request.Department;
            if (!string.IsNullOrWhiteSpace(request.Company)) user.Properties["company"].Value = request.Company;
            if (!string.IsNullOrWhiteSpace(request.Phone)) user.Properties["telephoneNumber"].Value = request.Phone;
            if (!string.IsNullOrWhiteSpace(request.Mobile)) user.Properties["mobile"].Value = request.Mobile;
            if (!string.IsNullOrWhiteSpace(request.Office)) user.Properties["physicalDeliveryOfficeName"].Value = request.Office;
            if (!string.IsNullOrWhiteSpace(request.Description)) user.Properties["description"].Value = request.Description;
            if (!string.IsNullOrWhiteSpace(request.ManagerDn)) user.Properties["manager"].Value = request.ManagerDn;

            int uac = request.Enabled ? 0x200 : 0x202;
            if (request.PasswordNeverExpires) uac |= 0x10000;
            user.Properties["userAccountControl"].Value = uac;
            user.Properties["pwdLastSet"].Value = 0;
            user.CommitChanges();

            user.Invoke("SetPassword", new object[] { request.Password });
            if (!request.MustChangePassword)
            {
                user.Properties["pwdLastSet"].Value = -1;
                user.CommitChanges();
            }

            return newDn;
        }, ct);
    }

    public Task DeleteUserAsync(AdUser user, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(user.DistinguishedName);
            entry.DeleteTree();
        }, ct);
    }

    public Task CreateGroupAsync(NewGroupRequest request, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var parent = OpenEntry(request.ParentDn);
            using var group = parent.Children.Add("CN=" + AdValue.EscapeDnValue(request.Name), "group");
            group.Properties["sAMAccountName"].Value = request.SamAccountName;
            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                group.Properties["description"].Value = request.Description;
            }

            int groupType = request.IsSecurity ? unchecked((int)0x80000000) : 0;
            groupType |= request.Scope switch
            {
                "DomainLocal" => 0x4,
                "Universal" => 0x8,
                _ => 0x2
            };
            group.Properties["groupType"].Value = groupType;
            group.CommitChanges();
        }, ct);
    }

    public Task DeleteGroupAsync(AdGroup group, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var entry = OpenEntry(group.DistinguishedName);
            entry.DeleteTree();
        }, ct);
    }

    public Task AddGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct)
        => AddToGroupAsync(groupDn, memberDn, ct);

    public Task RemoveGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct)
        => RemoveFromGroupAsync(groupDn, memberDn, ct);

    // ------------------------------------------------------------------
    //  Вспомогательное
    // ------------------------------------------------------------------

    private DirectoryEntry OpenEntry(string? dn)
    {
        string path = string.IsNullOrWhiteSpace(dn)
            ? "LDAP://" + _server
            : "LDAP://" + _server + "/" + dn;
        return new DirectoryEntry(path, null, null,
            AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing);
    }

    private static string BuildUserFilter(string? search, bool includeDisabled)
    {
        var sb = new System.Text.StringBuilder("(&(objectCategory=person)(objectClass=user)");
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = AdValue.EscapeLdapFilter(search.Trim());
            sb.Append("(|(displayName=*").Append(s).Append("*")
              .Append(")(sAMAccountName=*").Append(s).Append("*")
              .Append(")(userPrincipalName=*").Append(s).Append("*")
              .Append(")(mail=*").Append(s).Append("*")
              .Append(")(description=*").Append(s).Append("*")
              .Append(")(department=*").Append(s).Append("*")
              .Append(")(title=*").Append(s).Append("*))");
        }

        if (!includeDisabled)
        {
            sb.Append("(!(userAccountControl:1.2.840.113556.1.4.803:=2))");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string BuildGroupFilter(string? search)
    {
        var sb = new System.Text.StringBuilder("(&(objectCategory=group)(objectClass=group)");
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = AdValue.EscapeLdapFilter(search.Trim());
            sb.Append("(|(cn=*").Append(s).Append("*")
              .Append(")(sAMAccountName=*").Append(s).Append("*")
              .Append(")(description=*").Append(s).Append("*))");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static AdUser MapUser(SearchResult res)
    {
        int uac = Int(res, "userAccountControl");
        long pwdLastSet = Long(res, "pwdLastSet");
        long lockout = Long(res, "lockoutTime");
        bool hasUac = res.Properties.Contains("userAccountControl");
        bool hasPwdLastSet = res.Properties.Contains("pwdLastSet");
        var spn = Strings(res, "servicePrincipalName");
        var sidHistory = Bytes(res, "sIDHistory").Select(AdValue.SidToString).Where(s => s.Length > 0).ToList();
        var allowedDelegation = Strings(res, "msDS-AllowedToDelegateTo");

        var user = new AdUser
        {
            DistinguishedName = Str(res, "distinguishedName"),
            Name = Str(res, "cn"),
            DisplayName = FirstNonEmpty(Str(res, "displayName"), Str(res, "cn"), Str(res, "name")),
            FirstName = Str(res, "givenName"),
            LastName = Str(res, "sn"),
            InitialsRaw = Str(res, "initials"),
            SamAccountName = Str(res, "sAMAccountName"),
            UserPrincipalName = Str(res, "userPrincipalName"),
            Mail = Str(res, "mail"),
            Title = Str(res, "title"),
            Department = Str(res, "department"),
            Company = Str(res, "company"),
            Division = Str(res, "division"),
            ManagerDn = Str(res, "manager"),
            Phone = Str(res, "telephoneNumber"),
            Mobile = Str(res, "mobile"),
            IpPhone = Str(res, "ipPhone"),
            HomePhone = Str(res, "homePhone"),
            Fax = Str(res, "facsimileTelephoneNumber"),
            Office = Str(res, "physicalDeliveryOfficeName"),
            Street = Str(res, "streetAddress"),
            City = Str(res, "l"),
            State = Str(res, "st"),
            PostalCode = Str(res, "postalCode"),
            Country = Str(res, "co"),
            WebPage = Str(res, "wWWHomePage"),
            Description = Str(res, "description"),
            Notes = FirstNonEmpty(Str(res, "info"), Str(res, "notes")),
            EmployeeId = Str(res, "employeeID"),
            EmployeeType = Str(res, "employeeType"),
            ProfilePath = Str(res, "profilePath"),
            HomeDirectory = Str(res, "homeDirectory"),
            HomeDrive = Str(res, "homeDrive"),
            ScriptPath = Str(res, "scriptPath"),
            LogonWorkstations = Str(res, "userWorkstations"),
            LogonHours = Str(res, "logonHours"),
            MailNickname = Str(res, "mailNickname"),
            ProxyAddresses = string.Join("; ", Strings(res, "proxyAddresses")),
            TargetAddress = Str(res, "targetAddress"),
            HiddenFromGal = Bool(res, "msExchHideFromAddressLists"),
            UserAccountControl = uac,
            PrimaryGroupId = Int(res, "primaryGroupID"),
            Enabled = !hasUac || !AdValue.HasFlag(uac, 0x2),
            AccountStateReliable = hasUac,
            Locked = lockout > 0,
            PasswordNeverExpires = AdValue.HasFlag(uac, 0x10000),
            PasswordNotRequired = AdValue.HasFlag(uac, 0x20),
            SmartcardRequired = AdValue.HasFlag(uac, 0x40000),
            TrustedForDelegation = AdValue.HasFlag(uac, 0x80000),
            TrustedToAuthForDelegation = AdValue.HasFlag(uac, 0x1000000),
            DontRequirePreAuth = AdValue.HasFlag(uac, 0x400000),
            UseDesKeyOnly = AdValue.HasFlag(uac, 0x200000),
            NoAuthDataRequired = AdValue.HasFlag(uac, 0x2000000),
            AdminCount = Int(res, "adminCount") == 1,
            HasSpn = spn.Count > 0,
            HasSidHistory = sidHistory.Count > 0,
            HasConstrainedDelegation = allowedDelegation.Count > 0,
            WhenCreated = ToDateTime(First(res, "whenCreated")),
            WhenChanged = ToDateTime(First(res, "whenChanged")),
            LastLogon = AdValue.FileTimeToDateTime(Long(res, "lastLogon")),
            LastLogonTimestamp = AdValue.FileTimeToDateTime(Long(res, "lastLogonTimestamp")),
            LogonCount = Int(res, "logonCount"),
            BadPwdCount = Int(res, "badPwdCount"),
            LastBadPasswordAttempt = AdValue.FileTimeToDateTime(Long(res, "lastBadPasswordAttempt")),
            PasswordLastSet = pwdLastSet > 0 ? AdValue.FileTimeToDateTime(pwdLastSet) : null,
            PasswordExpires = AdValue.FileTimeToDateTime(Long(res, "msDS-UserPasswordExpiryTimeComputed")),
            AccountExpires = AdValue.AccountExpiresToDateTime(Long(res, "accountExpires")),
            LockoutTime = lockout > 0 ? AdValue.FileTimeToDateTime(lockout) : null,
            MustChangePassword = hasPwdLastSet && pwdLastSet == 0,
            MemberOf = Strings(res, "memberOf"),
            Spn = spn,
            AllowedToDelegateTo = allowedDelegation,
            SidHistory = sidHistory
        };

        user.Manager = AdValue.DnToName(user.ManagerDn);
        user.ObjectSid = AdValue.SidToString(First(res, "objectSid") as byte[]);
        user.ObjectGuid = AdValue.GuidToString(First(res, "objectGUID") as byte[]);
        if (user.PasswordNeverExpires)
        {
            user.PasswordExpires = null;
        }

        user.RefreshDerived();
        return user;
    }

    private static AdGroup MapGroup(SearchResult res)
    {
        int groupType = Int(res, "groupType");
        var group = new AdGroup
        {
            DistinguishedName = Str(res, "distinguishedName"),
            Name = FirstNonEmpty(Str(res, "cn"), Str(res, "name")),
            SamAccountName = Str(res, "sAMAccountName"),
            Description = Str(res, "description"),
            Notes = FirstNonEmpty(Str(res, "info"), Str(res, "notes")),
            GroupTypeRaw = groupType,
            GroupTypeText = AdValue.GroupTypeText(groupType),
            GroupScopeText = AdValue.GroupScopeText(groupType),
            IsSecurity = AdValue.IsSecurityGroup(groupType),
            ManagedByDn = Str(res, "managedBy"),
            WhenCreated = ToDateTime(First(res, "whenCreated")),
            WhenChanged = ToDateTime(First(res, "whenChanged")),
            MemberDns = Strings(res, "member"),
            MemberOf = Strings(res, "memberOf")
        };

        group.ManagedBy = AdValue.DnToName(group.ManagedByDn);
        group.ObjectSid = AdValue.SidToString(First(res, "objectSid") as byte[]);
        group.ObjectGuid = AdValue.GuidToString(First(res, "objectGUID") as byte[]);
        group.IsEmpty = group.MemberDns.Count == 0;
        group.HasNoOwner = string.IsNullOrWhiteSpace(group.ManagedByDn);
        group.RefreshDerived();
        return group;
    }

    private List<AdMember> ResolveDns(IReadOnlyList<string> dns, CancellationToken ct)
    {
        var result = new List<AdMember>(dns.Count);
        if (dns.Count == 0) return result;

        foreach (var batch in dns.Chunk(40))
        {
            ct.ThrowIfCancellationRequested();
            var map = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
            using (var searchRoot = OpenEntry(_rootDn))
            using (var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = "(|" + string.Concat(batch.Select(dn => "(distinguishedName=" + AdValue.EscapeLdapFilter(dn) + ")")) + ")",
                SearchScope = SearchScope.Subtree,
                PageSize = _pageSize,
                SizeLimit = batch.Length + 10,
                ServerTimeLimit = TimeSpan.FromSeconds(30)
            })
            {
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "distinguishedName", "objectClass", "sAMAccountName", "displayName", "name",
                    "department", "title", "memberOf"
                });

                foreach (SearchResult res in searcher.FindAll())
                {
                    string dn = Str(res, "distinguishedName");
                    if (!string.IsNullOrWhiteSpace(dn)) map[dn] = res;
                }
            }

            foreach (string dn in batch)
            {
                if (map.TryGetValue(dn, out SearchResult? res))
                {
                    var classes = Strings(res, "objectClass");
                    bool isGroup = classes.Any(c => c.Equals("group", StringComparison.OrdinalIgnoreCase));
                    result.Add(new AdMember
                    {
                        DistinguishedName = dn,
                        Name = FirstNonEmpty(Str(res, "displayName"), Str(res, "name"), AdValue.DnToName(dn)),
                        SamAccountName = Str(res, "sAMAccountName"),
                        ObjectClass = string.Join(", ", classes),
                        Department = Str(res, "department"),
                        Title = Str(res, "title"),
                        IsGroup = isGroup
                    });
                }
                else
                {
                    result.Add(new AdMember
                    {
                        DistinguishedName = dn,
                        Name = AdValue.DnToName(dn),
                        ObjectClass = "unknown"
                    });
                }
            }
        }

        return result;
    }

    private string? ResolvePrimaryGroupDn(int primaryGroupId)
    {
        if (primaryGroupId <= 0 || string.IsNullOrWhiteSpace(_domainSid))
        {
            return null;
        }

        try
        {
            string sid = _domainSid + "-" + primaryGroupId;
            using var root = OpenEntry(_rootDn);
            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=group)(objectSid=" + SidToLdapFilter(sid) + "))",
                SearchScope = SearchScope.Subtree,
                PageSize = 10,
                SizeLimit = 1,
                ServerTimeLimit = TimeSpan.FromSeconds(15),
                ClientTimeout = TimeSpan.FromSeconds(15)
            };
            searcher.PropertiesToLoad.Add("distinguishedName");
            SearchResult? res = searcher.FindOne();
            return res is null ? null : Str(res, "distinguishedName");
        }
        catch (Exception ex)
        {
            Warn("primaryGroup resolve: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    private List<string> ReadTokenGroupSids(string userDn)
    {
        var result = new List<string>();
        using var entry = OpenEntry(userDn);
        entry.RefreshCache(new[] { "tokenGroups" });
        foreach (object? value in entry.Properties["tokenGroups"])
        {
            if (value is byte[] bytes && bytes.Length > 0)
            {
                try
                {
                    result.Add(new SecurityIdentifier(bytes, 0).Value);
                }
                catch
                {
                    // Некорректный SID — пропускаем.
                }
            }
        }

        return result;
    }

    private List<string> ResolveGroupDnsBySids(IEnumerable<string> sids, CancellationToken ct)
    {
        var result = new List<string>();
        var unique = sids.Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();

        foreach (var chunk in unique.Chunk(40))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string filter = "(&(objectCategory=group)(|" +
                                string.Concat(chunk.Select(sid => "(objectSid=" + SidToLdapFilter(sid) + ")")) +
                                "))";
                using var root = OpenEntry(_rootDn);
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = filter,
                    SearchScope = SearchScope.Subtree,
                    PageSize = _pageSize,
                    SizeLimit = chunk.Length + 10,
                    ServerTimeLimit = TimeSpan.FromSeconds(30),
                    ClientTimeout = TimeSpan.FromSeconds(30)
                };
                searcher.PropertiesToLoad.Add("distinguishedName");
                foreach (SearchResult res in searcher.FindAll())
                {
                    string dn = Str(res, "distinguishedName");
                    if (!string.IsNullOrWhiteSpace(dn)) result.Add(dn);
                }
            }
            catch (Exception ex)
            {
                Warn("ResolveGroupDnsBySids: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        return result;
    }

    private static string SidToLdapFilter(string sid)
    {
        var identifier = new SecurityIdentifier(sid);
        byte[] bytes = new byte[identifier.BinaryLength];
        identifier.GetBinaryForm(bytes, 0);
        var sb = new System.Text.StringBuilder(bytes.Length * 3);
        foreach (byte b in bytes)
        {
            sb.Append('\\').Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    public Task<string> BuildDiagnosticsAsync(
        AdUser? user,
        AdGroup? group,
        string? baseDn,
        bool recursive,
        string? search,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AD CONNECTION ===");
            sb.AppendLine($"IsConnected={IsConnected}; IsDemo={IsDemo}; Domain={_domain}; Server={_server}");
            sb.AppendLine($"RootDn={_rootDn}; DomainSid={_domainSid}; CurrentUser={_currentUser}; PageSize={_pageSize}; Error={_error}");
            sb.AppendLine($"EventLogServer={_eventLogServer}");
            sb.AppendLine($"QueryBase={baseDn ?? "(null)"}; Recursive={recursive}; Search={(string.IsNullOrWhiteSpace(search) ? "(empty)" : search)}");
            sb.AppendLine();

            sb.AppendLine($"=== WARNINGS ({_warnings.Count}) ===");
            lock (_warnings)
            {
                foreach (string warning in _warnings)
                {
                    sb.AppendLine(warning);
                }
            }
            sb.AppendLine();

            DumpGroupQuery(sb, "UI BASE", baseDn, recursive, search, 300, ct);
            DumpGroupQuery(sb, "DOMAIN ROOT", _rootDn, true, null, 300, ct);

            if (user is not null)
            {
                DumpUserDiagnostics(sb, user, ct);
            }

            if (group is not null)
            {
                DumpGroupDiagnostics(sb, group, ct);
            }

            return sb.ToString();
        }, ct);
    }

    private void DumpGroupQuery(
        StringBuilder sb, string title, string? baseDn, bool recursive,
        string? search, int limit, CancellationToken ct)
    {
        sb.AppendLine($"=== GROUP QUERY: {title} ===");
        sb.AppendLine($"base={baseDn ?? "(null)"}; scope={(recursive ? "Subtree" : "OneLevel")}; search={(string.IsNullOrWhiteSpace(search) ? "(empty)" : search)}");
        try
        {
            using var root = OpenEntry(string.IsNullOrWhiteSpace(baseDn) ? _rootDn : baseDn);
            using var searcher = new DirectorySearcher(root)
            {
                Filter = BuildGroupFilter(search),
                SearchScope = recursive ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = _pageSize,
                SizeLimit = Math.Max(limit, 20000),
                ServerTimeLimit = TimeSpan.FromSeconds(60),
                ClientTimeout = TimeSpan.FromSeconds(60)
            };
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "distinguishedName", "cn", "sAMAccountName", "objectSid", "groupType",
                "member", "memberOf", "managedBy", "description"
            });

            int total = 0;
            int shown = 0;
            foreach (SearchResult res in searcher.FindAll())
            {
                ct.ThrowIfCancellationRequested();
                total++;
                if (shown >= limit) continue;

                shown++;
                sb.AppendLine($"{total}. cn={Str(res, "cn")} | sam={Str(res, "sAMAccountName")} | " +
                              $"sid={AdValue.SidToString(First(res, "objectSid") as byte[])} | " +
                              $"type=0x{Int(res, "groupType"):X} | members={Strings(res, "member").Count} | " +
                              $"memberOf={Strings(res, "memberOf").Count} | managedBy={Str(res, "managedBy")} | " +
                              $"dn={Str(res, "distinguishedName")}");
            }

            sb.AppendLine($"total={total}; shown={shown}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
        }

        sb.AppendLine();
    }

    private void DumpUserDiagnostics(StringBuilder sb, AdUser user, CancellationToken ct)
    {
        sb.AppendLine("=== SELECTED USER ===");
        sb.AppendLine($"DN={user.DistinguishedName}");
        sb.AppendLine($"UI: Sam={user.SamAccountName}; Enabled={user.Enabled}; Locked={user.Locked}; " +
                      $"Status={user.StatusText}; " +
                      $"UAC={user.UserAccountControl} (0x{user.UserAccountControl:X}); " +
                      $"LastLogonWorkstation={user.LastLogonWorkstation}; " +
                      $"MemberOf={user.MemberOf.Count}; Effective={user.EffectiveGroups.Count}; " +
                      $"PasswordNeverExpires={user.PasswordNeverExpires}; MustChangePassword={user.MustChangePassword}; " +
                      $"IsPasswordExpired={user.IsPasswordExpired}; PasswordExpires={user.PasswordExpires:O}; " +
                      $"AccountExpires={user.AccountExpires:O}");
        DumpUac(sb, user.UserAccountControl);

        sb.AppendLine("--- ATTRIBUTE ACCESS TEST ---");
        DumpAttributeAccess(sb, user.DistinguishedName, AuthenticationTypes.Secure, "Secure");
        DumpAttributeAccess(sb, user.DistinguishedName,
            AuthenticationTypes.Secure | AuthenticationTypes.Signing | AuthenticationTypes.Sealing,
            "Secure+Signing+Sealing");

        try
        {
            using var entry = OpenEntry(user.DistinguishedName);
            string[] attrs =
            {
                "distinguishedName", "cn", "displayName", "sAMAccountName", "userPrincipalName",
                "userAccountControl", "lockoutTime", "pwdLastSet", "msDS-UserPasswordExpiryTimeComputed",
                "accountExpires", "primaryGroupID", "objectSid", "whenCreated", "whenChanged",
                "badPwdCount", "logonCount", "lastLogon", "lastLogonTimestamp", "memberOf"
            };
            entry.RefreshCache(attrs);

            foreach (string attr in attrs)
            {
                if (attr.Equals("memberOf", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var values = new List<string>();
                    foreach (object? value in entry.Properties[attr])
                    {
                        values.Add(AdValue.FormatAttributeValue(attr, value));
                    }

                    sb.AppendLine($"{attr}={string.Join("; ", values.Take(20))}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"{attr}=ERROR: {ex.Message}");
                }
            }

            int rawUac = AdValue.GetInt(First(entry, "userAccountControl"));
            sb.AppendLine($"RAW decoded: Enabled={!AdValue.HasFlag(rawUac, 0x2)}; " +
                          $"Locked={AdValue.GetInt(First(entry, "lockoutTime")) > 0}; " +
                          $"PasswordNeverExpires={AdValue.HasFlag(rawUac, 0x10000)}");

            var memberDns = ReadStrings(entry, "memberOf");
            sb.AppendLine($"RAW memberOf count={memberDns.Count}");
            foreach (string dn in memberDns)
            {
                sb.AppendLine("  " + DescribeDn(dn));
            }

            int primaryGroupId = AdValue.GetInt(First(entry, "primaryGroupID"));
            string? primaryGroupDn = ResolvePrimaryGroupDn(primaryGroupId);
            sb.AppendLine($"primaryGroupID={primaryGroupId}; resolved={primaryGroupDn ?? "(not resolved)"}");
            if (!string.IsNullOrWhiteSpace(primaryGroupDn))
            {
                sb.AppendLine("  " + DescribeDn(primaryGroupDn));
            }

            sb.AppendLine("--- tokenGroups ---");
            try
            {
                entry.RefreshCache(new[] { "tokenGroups" });
                int tokens = 0;
                foreach (object? value in entry.Properties["tokenGroups"])
                {
                    if (value is not byte[] bytes || bytes.Length == 0) continue;
                    tokens++;
                    string sid;
                    string name;
                    try
                    {
                        var identifier = new SecurityIdentifier(bytes, 0);
                        sid = identifier.Value;
                        try
                        {
                            name = identifier.Translate(typeof(NTAccount)).Value;
                        }
                        catch
                        {
                            name = "(нет перевода)";
                        }
                    }
                    catch
                    {
                        sid = "(invalid)";
                        name = "?";
                    }

                    sb.AppendLine($"  {sid} = {name}");
                }

                sb.AppendLine($"tokenGroups count={tokens}");
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex);
            }

            sb.AppendLine("--- AccountManagement ---");
            sb.AppendLine("Отключён: GetAuthorizationGroups выполняет S4U-вход от имени пользователя и создаёт события 4648. " +
                          "Эффективные группы читаются через tokenGroups.");
        }
        catch (Exception ex)
        {
            sb.AppendLine("USER RAW ERROR: " + ex);
        }

        sb.AppendLine();
    }

    private void DumpGroupDiagnostics(StringBuilder sb, AdGroup group, CancellationToken ct)
    {
        sb.AppendLine("=== SELECTED GROUP ===");
        sb.AppendLine($"UI: Name={group.Name}; Sam={group.SamAccountName}; SID={group.ObjectSid}; " +
                      $"DN={group.DistinguishedName}; Members={group.MemberCount}; " +
                      $"IsPrivileged={group.IsPrivileged}; IsNestedPrivileged={group.IsNestedPrivileged}; " +
                      $"Type={group.GroupTypeText}; Scope={group.GroupScopeText}; ManagedBy={group.ManagedByDn}");
        try
        {
            using var entry = OpenEntry(group.DistinguishedName);
            string[] attrs = { "cn", "sAMAccountName", "objectSid", "groupType", "managedBy", "description", "member", "memberOf" };
            entry.RefreshCache(attrs);
            var members = ReadStrings(entry, "member");
            sb.AppendLine($"RAW: type=0x{AdValue.GetInt(First(entry, "groupType")):X}; members={members.Count}; " +
                          $"memberOf={ReadStrings(entry, "memberOf").Count}; managedBy={AdValue.GetString(First(entry, "managedBy"))}");
            foreach (string dn in members.Take(100))
            {
                sb.AppendLine("  member: " + DescribeDn(dn));
            }

            string? parent = AdValue.DnToOu(group.DistinguishedName);
            sb.AppendLine($"OU={parent}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("GROUP RAW ERROR: " + ex);
        }

        sb.AppendLine();
    }

    private void DumpAttributeAccess(StringBuilder sb, string dn, AuthenticationTypes flags, string label)
    {
        try
        {
            using var entry = new DirectoryEntry("LDAP://" + _server + "/" + dn, null, null, flags);
            entry.RefreshCache(new[] { "userAccountControl", "pwdLastSet", "memberOf", "tokenGroups", "whenCreated" });

            string uac = entry.Properties["userAccountControl"].Count > 0
                ? AdValue.GetString(entry.Properties["userAccountControl"].Value)
                : "(missing)";
            string pwd = entry.Properties["pwdLastSet"].Count > 0
                ? AdValue.GetString(entry.Properties["pwdLastSet"].Value)
                : "(missing)";
            sb.AppendLine($"{label}: UAC={uac}; pwdLastSet={pwd}; " +
                          $"memberOf={ReadStrings(entry, "memberOf").Count}; " +
                          $"tokenGroups={entry.Properties["tokenGroups"].Count}; " +
                          $"whenCreated={AdValue.GetString(entry.Properties["whenCreated"].Value)}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{label}: ERROR {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpUac(StringBuilder sb, int uac)
    {        (int Mask, string Name)[] flags =
        {
            (0x1, "SCRIPT"),
            (0x2, "ACCOUNTDISABLE"),
            (0x8, "HOMEDIR_REQUIRED"),
            (0x10, "LOCKOUT"),
            (0x20, "PASSWD_NOTREQD"),
            (0x40, "PASSWD_CANT_CHANGE"),
            (0x80, "ENCRYPTED_TEXT_PASSWORD_ALLOWED"),
            (0x100, "TEMP_DUPLICATE_ACCOUNT"),
            (0x200, "NORMAL_ACCOUNT"),
            (0x800, "INTERDOMAIN_TRUST_ACCOUNT"),
            (0x1000, "WORKSTATION_TRUST_ACCOUNT"),
            (0x2000, "SERVER_TRUST_ACCOUNT"),
            (0x10000, "DONT_EXPIRE_PASSWORD"),
            (0x20000, "MNS_LOGON_ACCOUNT"),
            (0x40000, "SMARTCARD_REQUIRED"),
            (0x80000, "TRUSTED_FOR_DELEGATION"),
            (0x100000, "NOT_DELEGATED"),
            (0x200000, "USE_DES_KEY_ONLY"),
            (0x400000, "DONT_REQUIRE_PREAUTH"),
            (0x800000, "PASSWORD_EXPIRED"),
            (0x1000000, "TRUSTED_TO_AUTH_FOR_DELEGATION"),
            (0x2000000, "NO_AUTH_DATA_REQUIRED"),
            (0x4000000, "PARTIAL_SECRETS_ACCOUNT")
        };

        string set = string.Join(" | ", flags.Where(f => (uac & f.Mask) == f.Mask).Select(f => f.Name));
        sb.AppendLine("UAC decode: " + (set.Length == 0 ? "(none)" : set));
    }

    private string DescribeDn(string dn)
    {
        try
        {
            using var entry = OpenEntry(dn);
            entry.RefreshCache(new[] { "cn", "sAMAccountName", "objectSid", "groupType", "member" });
            return $"cn={AdValue.GetString(First(entry, "cn"))}; " +
                   $"sam={AdValue.GetString(First(entry, "sAMAccountName"))}; " +
                   $"sid={AdValue.SidToString(First(entry, "objectSid") as byte[])}; " +
                   $"type=0x{AdValue.GetInt(First(entry, "groupType")):X}; " +
                   $"members={ReadStrings(entry, "member").Count}; dn={dn}";
        }
        catch (Exception ex)
        {
            return $"{dn} -> ERROR: {ex.Message}";
        }
    }

    private string? FindLastLogonWorkstation(string samAccountName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_eventLogServer) || string.IsNullOrWhiteSpace(samAccountName))
        {
            return string.Empty;
        }

        lock (_lastLogonCache)
        {
            if (_lastLogonCache.TryGetValue(samAccountName, out var cached) &&
                DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(10))
            {
                return cached.Value;
            }
        }

        string Store(string value)
        {
            lock (_lastLogonCache)
            {
                _lastLogonCache[samAccountName] = (value, DateTime.UtcNow);
            }

            return value;
        }
        try
        {
            string since = DateTime.UtcNow.AddDays(-180).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string query = "*[System[(EventID=4624 or EventID=4768) and " +
                           $"TimeCreated[@SystemTime>='{since}']]] and " +
                           $"*[EventData[Data[@Name='TargetUserName']=\"{samAccountName}\"]]";

            using var session = new EventLogSession(_eventLogServer);
            var logQuery = new EventLogQuery("Security", PathType.LogName, query)
            {
                Session = session,
                TolerateQueryErrors = true
            };

            using var reader = new EventLogReader(logQuery);
            DateTime? latest = null;
            string? workstation = null;
            string? ipAddress = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);

            EventRecord? record;
            while ((record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > deadline) break;
                    if (record.TimeCreated is not DateTime created) continue;
                    if (latest.HasValue && created <= latest.Value) continue;

                    XDocument document;
                    try
                    {
                        document = XDocument.Parse(record.ToXml());
                    }
                    catch
                    {
                        continue;
                    }

                    var data = document.Descendants()
                        .Where(element => element.Name.LocalName == "Data" &&
                                          element.Attribute("Name") is not null)
                        .GroupBy(element => element.Attribute("Name")!.Value, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

                    if (!data.TryGetValue("TargetUserName", out string? target) ||
                        !target.Equals(samAccountName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? eventWorkstation = data.GetValueOrDefault("WorkstationName");
                    string? eventIp = data.GetValueOrDefault("IpAddress");
                    eventWorkstation = NormalizeLogonSource(eventWorkstation);
                    eventIp = NormalizeLogonSource(eventIp);

                    if (eventWorkstation is null && eventIp is null) continue;

                    latest = created;
                    workstation = eventWorkstation;
                    ipAddress = eventIp;
                }
            }

            if (latest is null) return Store("нет данных в журнале Security");

            string machine = !string.IsNullOrWhiteSpace(workstation)
                ? workstation!
                : "IP " + ipAddress;
            if (!string.IsNullOrWhiteSpace(workstation) && !string.IsNullOrWhiteSpace(ipAddress))
            {
                machine += " (" + ipAddress + ")";
            }

            return Store(machine + " · " + AdValue.ToLocalText(latest));
        }
        catch (UnauthorizedAccessException ex)
        {
            Warn("LastLogonWorkstation access: " + ex.Message);
            return Store("нет прав на журнал Security");
        }
        catch (Exception ex)
        {
            Warn("LastLogonWorkstation: " + ex.GetType().Name + ": " + ex.Message);
            return Store("не определён");
        }
    }

    private static string? NormalizeLogonSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-") return null;
        return value.Trim();
    }

    private string? ResolveUserDn(string samOrDn)
    {
        if (samOrDn.Contains('=')) return samOrDn;
        try
        {
            using var searchRoot = OpenEntry(_rootDn);
            using var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = "(&(objectCategory=person)(objectClass=user)(sAMAccountName=" +
                         AdValue.EscapeLdapFilter(samOrDn) + "))",
                SearchScope = SearchScope.Subtree,
                PageSize = 10
            };
            searcher.PropertiesToLoad.Add("distinguishedName");
            SearchResult? res = searcher.FindOne();
            return res is null ? null : Str(res, "distinguishedName");
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadMemberDns(DirectoryEntry entry)
    {
        const int page = 1499;

        // Для небольших групп AD отдаёт обычный атрибут member. Для больших —
        // member;range=0-1499 и далее постранично.
        var direct = ReadStrings(entry, "member");
        if (direct.Count < page)
        {
            return direct;
        }

        var result = new List<string>(direct.Count + page);
        int start = 0;
        while (true)
        {
            string range = $"member;range={start}-{start + page}";
            try
            {
                entry.RefreshCache(new[] { range });
            }
            catch (COMException)
            {
                break;
            }

            string? actual = entry.Properties.PropertyNames
                .Cast<string>()
                .FirstOrDefault(n => n.Equals("member", StringComparison.OrdinalIgnoreCase) ||
                                     n.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase));

            if (actual is null)
            {
                // Небольшая группа: диапазон не пришёл, читаем обычный member.
                foreach (string dn in ReadStrings(entry, "member"))
                {
                    if (!result.Contains(dn, StringComparer.OrdinalIgnoreCase)) result.Add(dn);
                }

                break;
            }

            int before = result.Count;
            foreach (object? value in entry.Properties[actual])
            {
                if (value is string dn && !result.Contains(dn, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(dn);
                }
            }

            if (actual.Equals("member", StringComparison.OrdinalIgnoreCase) ||
                actual.EndsWith("-*", StringComparison.Ordinal))
            {
                break;
            }

            if (result.Count - before < page + 1)
            {
                break;
            }

            start += page + 1;
        }

        // Если постраничное чтение не дало ничего, оставляем данные из поиска.
        return result.Count > 0 ? result : direct;
    }

    private static List<string> ReadStrings(DirectoryEntry entry, string property)
    {
        var result = new List<string>();
        try
        {
            foreach (object? value in entry.Properties[property])
            {
                if (value is string s) result.Add(s);
            }
        }
        catch
        {
            // Свойства может не быть.
        }

        return result;
    }

    private static object? First(SearchResult res, string name)
    {
        try
        {
            return res.Properties.Contains(name) && res.Properties[name].Count > 0
                ? res.Properties[name][0]
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static object? First(DirectoryEntry entry, string name)
    {
        try
        {
            return entry.Properties.Contains(name) && entry.Properties[name].Count > 0
                ? entry.Properties[name].Value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Str(SearchResult res, string name) => AdValue.GetString(First(res, name));
    private static int Int(SearchResult res, string name) => AdValue.GetInt(First(res, name));
    private static long Long(SearchResult res, string name) => AdValue.GetLong(First(res, name));

    private static List<string> Strings(SearchResult res, string name)
    {
        var result = new List<string>();
        try
        {
            if (!res.Properties.Contains(name)) return result;
            foreach (object? value in res.Properties[name])
            {
                string s = AdValue.GetString(value);
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
        }
        catch
        {
            // Игнорируем отсутствующие/битые значения.
        }

        return result;
    }

    private static List<byte[]> Bytes(SearchResult res, string name)
    {
        var result = new List<byte[]>();
        try
        {
            if (!res.Properties.Contains(name)) return result;
            foreach (object? value in res.Properties[name])
            {
                if (value is byte[] bytes) result.Add(bytes);
            }
        }
        catch
        {
            // Игнорируем.
        }

        return result;
    }

    private static bool Bool(SearchResult res, string name)
    {
        object? value = First(res, name);
        if (value is bool b) return b;
        string s = AdValue.GetString(value);
        return s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1";
    }

    private static DateTime? ToDateTime(object? value)
    {
        if (value is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
        }

        if (value is string s && DateTime.TryParse(s, out DateTime parsed))
        {
            return parsed.Kind == DateTimeKind.Local ? parsed.ToUniversalTime() : parsed;
        }

        return null;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    public void Dispose()
    {
        _root?.Dispose();
        _root = null;
        IsConnected = false;
    }
}
