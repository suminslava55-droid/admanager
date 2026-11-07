using AdManager.Common;
using AdManager.Models;

namespace AdManager.Services;

/// <summary>Демонстрационный источник данных: позволяет посмотреть интерфейс без домена.</summary>
public sealed class DemoAdDirectoryService : IAdDirectoryService
{
    private const string Root = "DC=demo,DC=local";
    private const string UsersOu = "OU=Users," + Root;
    private const string GroupsOu = "OU=Groups," + Root;

    private readonly List<AdUser> _users = new();
    private readonly List<AdGroup> _groups = new();
    private readonly List<AdOu> _ous = new();

    public DemoAdDirectoryService()
    {
        BuildOus();
        BuildUsers();
        BuildGroups();
        LinkMembership();
    }

    public bool IsConnected { get; private set; }
    public bool IsDemo => true;
    public string DomainName => "DEMO";
    public string ServerName => "demo-dc";
    public string RootDn => Root;
    public string CurrentUser => Environment.UserName;
    public string ConnectionError => string.Empty;

    public Task ConnectAsync(string? domain, string? server, CancellationToken ct)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<List<AdOu>> LoadOusAsync(CancellationToken ct)
        => Task.FromResult(_ous.ToList());

    public Task<List<AdUser>> LoadUsersAsync(
        string? baseDn,
        bool recursive,
        string? search,
        bool includeDisabled,
        int max,
        CancellationToken ct)
    {
        IEnumerable<AdUser> query = _users;
        if (!string.IsNullOrWhiteSpace(baseDn))
        {
            query = query.Where(u => recursive
                ? u.DistinguishedName.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase)
                : u.DistinguishedName.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase)
                  && AdValue.DnParent(u.DistinguishedName).Equals(baseDn, StringComparison.OrdinalIgnoreCase));
        }

        if (!includeDisabled) query = query.Where(u => u.Enabled);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.SearchBlob.Contains(s, StringComparison.Ordinal));
        }

        return Task.FromResult(query.Take(Math.Max(1, max)).ToList());
    }

    public Task<List<AdGroup>> LoadGroupsAsync(
        string? baseDn,
        bool recursive,
        string? search,
        int max,
        CancellationToken ct)
    {
        IEnumerable<AdGroup> query = _groups;
        if (!string.IsNullOrWhiteSpace(baseDn))
        {
            query = query.Where(g => recursive
                ? g.DistinguishedName.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase)
                : g.DistinguishedName.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase)
                  && AdValue.DnParent(g.DistinguishedName).Equals(baseDn, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLowerInvariant();
            query = query.Where(g => g.SearchBlob.Contains(s, StringComparison.Ordinal));
        }

        return Task.FromResult(query.Take(Math.Max(1, max)).ToList());
    }

    public Task<List<AdGroup>> SearchGroupsAsync(string? search, int max, CancellationToken ct)
        => LoadGroupsAsync(Root, true, search, max, ct);

    public Task LoadUserDetailsAsync(AdUser user, CancellationToken ct)
    {
        user.AllAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["distinguishedName"] = user.DistinguishedName,
            ["cn"] = user.Name,
            ["displayName"] = user.DisplayName,
            ["sAMAccountName"] = user.SamAccountName,
            ["userPrincipalName"] = user.UserPrincipalName,
            ["mail"] = user.Mail,
            ["title"] = user.Title,
            ["department"] = user.Department,
            ["company"] = user.Company,
            ["manager"] = user.ManagerDn,
            ["telephoneNumber"] = user.Phone,
            ["mobile"] = user.Mobile,
            ["physicalDeliveryOfficeName"] = user.Office,
            ["description"] = user.Description,
            ["userAccountControl"] = user.UserAccountControl.ToString(),
            ["objectSid"] = user.ObjectSid,
            ["objectGUID"] = user.ObjectGuid,
            ["whenCreated"] = user.WhenCreatedText,
            ["whenChanged"] = user.WhenChangedText,
            ["lastLogon"] = user.LastLogonText,
            ["pwdLastSet"] = user.PasswordLastSetText,
            ["accountExpires"] = user.AccountExpiresText,
            ["memberOf"] = string.Join("; ", user.MemberOf),
            ["servicePrincipalName"] = string.Join("; ", user.Spn)
        };

        user.AllAttributeItems = user.AllAttributes
            .Select(kv => new AttributeItem(kv.Key, kv.Value))
            .OrderBy(i => i.Name)
            .ToList();
        user.DetailsLoaded = true;
        user.RaiseAll();
        return Task.CompletedTask;
    }

    public Task LoadGroupDetailsAsync(AdGroup group, CancellationToken ct)
    {
        group.AllAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["distinguishedName"] = group.DistinguishedName,
            ["cn"] = group.Name,
            ["sAMAccountName"] = group.SamAccountName,
            ["description"] = group.Description,
            ["groupType"] = group.GroupTypeRaw.ToString(),
            ["managedBy"] = group.ManagedByDn,
            ["member"] = string.Join("; ", group.MemberDns),
            ["memberOf"] = string.Join("; ", group.MemberOf),
            ["objectSid"] = group.ObjectSid,
            ["objectGUID"] = group.ObjectGuid,
            ["whenCreated"] = group.WhenCreatedText,
            ["whenChanged"] = group.WhenChangedText
        };

        group.AllAttributeItems = group.AllAttributes
            .Select(kv => new AttributeItem(kv.Key, kv.Value))
            .OrderBy(i => i.Name)
            .ToList();
        group.DetailsLoaded = true;
        group.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task<List<AdMember>> LoadGroupMembersAsync(AdGroup group, CancellationToken ct)
    {
        var list = new List<AdMember>();
        foreach (string dn in group.MemberDns)
        {
            AdUser? user = _users.FirstOrDefault(u => u.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase));
            if (user is not null)
            {
                list.Add(new AdMember
                {
                    DistinguishedName = dn,
                    Name = user.DisplayLabel,
                    SamAccountName = user.SamAccountName,
                    ObjectClass = "user",
                    Department = user.Department,
                    Title = user.Title,
                    IsGroup = false,
                    IsPrivileged = user.IsPrivileged
                });
                continue;
            }

            AdGroup? child = _groups.FirstOrDefault(g => g.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase));
            if (child is not null)
            {
                list.Add(new AdMember
                {
                    DistinguishedName = dn,
                    Name = child.Name,
                    SamAccountName = child.SamAccountName,
                    ObjectClass = "group",
                    IsGroup = true,
                    IsPrivileged = child.IsPrivileged
                });
                continue;
            }

            list.Add(new AdMember
            {
                DistinguishedName = dn,
                Name = AdValue.DnToName(dn),
                ObjectClass = "unknown"
            });
        }

        group.Members = list;
        group.RaiseAll();
        return Task.FromResult(list);
    }

    public Task<List<string>> LoadUserEffectiveGroupDnsAsync(AdUser user, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var group in _groups.Where(g => g.MemberDns.Contains(user.DistinguishedName, StringComparer.OrdinalIgnoreCase)))
        {
            queue.Enqueue(group.DistinguishedName);
        }

        while (queue.Count > 0)
        {
            string dn = queue.Dequeue();
            if (!result.Add(dn)) continue;
            foreach (var parent in _groups.Where(g => g.MemberDns.Contains(dn, StringComparer.OrdinalIgnoreCase)))
            {
                queue.Enqueue(parent.DistinguishedName);
            }
        }

        return Task.FromResult(result.ToList());
    }

    public Task<string> BuildDiagnosticsAsync(
        AdUser? user,
        AdGroup? group,
        string? baseDn,
        bool recursive,
        string? search,
        CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== DEMO MODE ===");
        sb.AppendLine($"Users={_users.Count}; Groups={_groups.Count}; Base={baseDn}; Recursive={recursive}; Search={search}");
        if (user is not null)
        {
            sb.AppendLine($"User: {user.DisplayLabel} | {user.SamAccountName} | Enabled={user.Enabled} | UAC=0x{user.UserAccountControl:X}");
            sb.AppendLine($"MemberOf={user.MemberOf.Count}; Effective={user.EffectiveGroups.Count}");
            foreach (string dn in user.MemberOf) sb.AppendLine("  " + dn);
        }

        if (group is not null)
        {
            sb.AppendLine($"Group: {group.Name} | {group.SamAccountName} | Members={group.MemberCount} | SID={group.ObjectSid}");
        }

        return Task.FromResult(sb.ToString());
    }

    public Task SetUserEnabledAsync(AdUser user, bool enabled, CancellationToken ct)
    {
        user.Enabled = enabled;
        user.UserAccountControl = enabled
            ? user.UserAccountControl & ~0x2
            : user.UserAccountControl | 0x2;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task UnlockUserAsync(AdUser user, CancellationToken ct)
    {
        user.Locked = false;
        user.LockoutTime = null;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task ResetPasswordAsync(AdUser user, string password, bool mustChange, CancellationToken ct)
    {
        user.PasswordLastSet = DateTime.UtcNow;
        user.MustChangePassword = mustChange;
        user.IsPasswordExpired = false;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task SetPasswordNeverExpiresAsync(AdUser user, bool value, CancellationToken ct)
    {
        user.PasswordNeverExpires = value;
        user.UserAccountControl = value
            ? user.UserAccountControl | 0x10000
            : user.UserAccountControl & ~0x10000;
        user.PasswordExpires = value ? null : DateTime.UtcNow.AddDays(42);
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task SetMustChangePasswordAsync(AdUser user, CancellationToken ct)
    {
        user.MustChangePassword = true;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task ResetPasswordAgeAsync(AdUser user, CancellationToken ct)
    {
        user.PasswordLastSet = DateTime.UtcNow;
        user.PasswordExpires = user.PasswordNeverExpires ? null : DateTime.UtcNow.AddDays(42);
        user.MustChangePassword = false;
        user.IsPasswordExpired = false;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task SetAccountExpirationAsync(AdUser user, DateTime? expiration, CancellationToken ct)
    {
        user.AccountExpires = expiration;
        user.IsAccountExpired = expiration.HasValue && expiration.Value.ToUniversalTime() < DateTime.UtcNow;
        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task MoveObjectAsync(string dn, string newParentDn, CancellationToken ct)
        => throw new NotSupportedException("В демо-режиме перемещение объектов отключено.");

    public Task AddToGroupAsync(string groupDn, string memberDn, CancellationToken ct)
    {
        AdGroup? group = _groups.FirstOrDefault(g => g.DistinguishedName.Equals(groupDn, StringComparison.OrdinalIgnoreCase));
        if (group is not null && !group.MemberDns.Contains(memberDn, StringComparer.OrdinalIgnoreCase))
        {
            group.MemberDns.Add(memberDn);
            AdUser? user = _users.FirstOrDefault(u => u.DistinguishedName.Equals(memberDn, StringComparison.OrdinalIgnoreCase));
            user?.MemberOf.Add(groupDn);
            group.RefreshDerived();
            user?.RefreshDerived();
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string groupDn, string memberDn, CancellationToken ct)
    {
        AdGroup? group = _groups.FirstOrDefault(g => g.DistinguishedName.Equals(groupDn, StringComparison.OrdinalIgnoreCase));
        if (group is not null && group.MemberDns.RemoveAll(d => d.Equals(memberDn, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            AdUser? user = _users.FirstOrDefault(u => u.DistinguishedName.Equals(memberDn, StringComparison.OrdinalIgnoreCase));
            user?.MemberOf.RemoveAll(d => d.Equals(groupDn, StringComparison.OrdinalIgnoreCase));
            group.RefreshDerived();
            user?.RefreshDerived();
        }

        return Task.CompletedTask;
    }

    public Task UpdateUserAttributesAsync(AdUser user, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct)
    {
        foreach (var pair in attributes)
        {
            string? value = pair.Value ?? string.Empty;
            switch (pair.Key)
            {
                case "displayName": user.DisplayName = value; break;
                case "givenName": user.FirstName = value; break;
                case "sn": user.LastName = value; break;
                case "sAMAccountName": user.SamAccountName = value; break;
                case "userPrincipalName": user.UserPrincipalName = value; break;
                case "mail": user.Mail = value; break;
                case "title": user.Title = value; break;
                case "department": user.Department = value; break;
                case "company": user.Company = value; break;
                case "telephoneNumber": user.Phone = value; break;
                case "mobile": user.Mobile = value; break;
                case "physicalDeliveryOfficeName": user.Office = value; break;
                case "description": user.Description = value; break;
                case "manager": user.ManagerDn = value; user.Manager = AdValue.DnToName(value); break;
                case "employeeID": user.EmployeeId = value; break;
                case "employeeType": user.EmployeeType = value; break;
                case "profilePath": user.ProfilePath = value; break;
                case "homeDirectory": user.HomeDirectory = value; break;
                case "homeDrive": user.HomeDrive = value; break;
                case "scriptPath": user.ScriptPath = value; break;
                case "userWorkstations": user.LogonWorkstations = value; break;
            }
        }

        user.RefreshDerived();
        return Task.CompletedTask;
    }

    public Task<string> CreateUserAsync(NewUserRequest request, CancellationToken ct)
    {
        string cn = string.IsNullOrWhiteSpace(request.Cn) ? request.DisplayName : request.Cn;
        var user = new AdUser
        {
            DistinguishedName = $"CN={AdValue.EscapeDnValue(cn)},{request.ParentDn}",
            Name = cn,
            DisplayName = request.DisplayName,
            FirstName = request.FirstName,
            LastName = request.LastName,
            SamAccountName = request.SamAccountName,
            UserPrincipalName = request.UserPrincipalName,
            Mail = request.Mail,
            Title = request.Title,
            Department = request.Department,
            Company = request.Company,
            Phone = request.Phone,
            Mobile = request.Mobile,
            Office = request.Office,
            Description = request.Description,
            ManagerDn = request.ManagerDn,
            Manager = AdValue.DnToName(request.ManagerDn),
            Enabled = request.Enabled,
            UserAccountControl = request.Enabled ? 0x200 : 0x202,
            WhenCreated = DateTime.UtcNow,
            WhenChanged = DateTime.UtcNow,
            PasswordLastSet = DateTime.UtcNow,
            MustChangePassword = request.MustChangePassword,
            PasswordNeverExpires = request.PasswordNeverExpires
        };
        if (request.PasswordNeverExpires) user.UserAccountControl |= 0x10000;
        user.ObjectSid = "S-1-5-21-1000-1000-1000-" + (_users.Count + 1000);
        user.ObjectGuid = Guid.NewGuid().ToString("D");
        _users.Add(user);
        return Task.FromResult(user.DistinguishedName);
    }

    public Task DeleteUserAsync(AdUser user, CancellationToken ct)
    {
        _users.RemoveAll(u => u.DistinguishedName.Equals(user.DistinguishedName, StringComparison.OrdinalIgnoreCase));
        foreach (var group in _groups)
        {
            group.MemberDns.RemoveAll(d => d.Equals(user.DistinguishedName, StringComparison.OrdinalIgnoreCase));
            group.RefreshDerived();
        }

        return Task.CompletedTask;
    }

    public Task CreateGroupAsync(NewGroupRequest request, CancellationToken ct)
    {
        int groupType = request.IsSecurity ? unchecked((int)0x80000000) : 0;
        groupType |= request.Scope switch
        {
            "DomainLocal" => 0x4,
            "Universal" => 0x8,
            _ => 0x2
        };

        var group = new AdGroup
        {
            DistinguishedName = $"CN={AdValue.EscapeDnValue(request.Name)},{request.ParentDn}",
            Name = request.Name,
            SamAccountName = request.SamAccountName,
            Description = request.Description,
            GroupTypeRaw = groupType,
            GroupTypeText = AdValue.GroupTypeText(groupType),
            GroupScopeText = AdValue.GroupScopeText(groupType),
            IsSecurity = request.IsSecurity,
            IsEmpty = true,
            HasNoOwner = true,
            WhenCreated = DateTime.UtcNow,
            WhenChanged = DateTime.UtcNow,
            ObjectSid = "S-1-5-21-1000-1000-1000-" + (_groups.Count + 2000),
            ObjectGuid = Guid.NewGuid().ToString("D")
        };
        _groups.Add(group);
        return Task.CompletedTask;
    }

    public Task DeleteGroupAsync(AdGroup group, CancellationToken ct)
    {
        _groups.RemoveAll(g => g.DistinguishedName.Equals(group.DistinguishedName, StringComparison.OrdinalIgnoreCase));
        foreach (var user in _users)
        {
            user.MemberOf.RemoveAll(d => d.Equals(group.DistinguishedName, StringComparison.OrdinalIgnoreCase));
            user.RefreshDerived();
        }

        return Task.CompletedTask;
    }

    public Task AddGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct)
        => AddToGroupAsync(groupDn, memberDn, ct);

    public Task RemoveGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct)
        => RemoveFromGroupAsync(groupDn, memberDn, ct);

    // ------------------------------------------------------------------
    //  Демо-данные
    // ------------------------------------------------------------------

    private void BuildOus()
    {
        void Add(string name, string dn, string parent)
            => _ous.Add(new AdOu { Name = name, DistinguishedName = dn, ParentDn = parent, IsExpanded = true });

        Add("DEMO", Root, "");
        Add("Users", UsersOu, Root);
        Add("IT", "OU=IT," + UsersOu, UsersOu);
        Add("Sales", "OU=Sales," + UsersOu, UsersOu);
        Add("Finance", "OU=Finance," + UsersOu, UsersOu);
        Add("Service Accounts", "OU=ServiceAccounts," + UsersOu, UsersOu);
        Add("Disabled", "OU=Disabled," + UsersOu, UsersOu);
        Add("Groups", GroupsOu, Root);
    }

    private void BuildUsers()
    {
        var now = DateTime.UtcNow;

        AdUser Add(
            string display, string sam, string ou, string title, string department,
            bool enabled = true, int lastLogonDays = -5, int pwdDays = -30,
            string? managerDn = null, string? mail = null)
        {
            string dn = $"CN={AdValue.EscapeDnValue(display)},{ou}";
            var user = new AdUser
            {
                DistinguishedName = dn,
                Name = display,
                DisplayName = display,
                SamAccountName = sam,
                UserPrincipalName = sam + "@demo.local",
                Mail = mail ?? sam + "@demo.local",
                Title = title,
                Department = department,
                Enabled = enabled,
                UserAccountControl = enabled ? 0x200 : 0x202,
                WhenCreated = now.AddDays(-400),
                WhenChanged = now.AddDays(-10),
                LastLogon = lastLogonDays < 0 ? now.AddDays(lastLogonDays) : null,
                LastLogonTimestamp = lastLogonDays < 0 ? now.AddDays(lastLogonDays) : null,
                LogonCount = lastLogonDays < 0 ? 42 : 0,
                PasswordLastSet = pwdDays < 0 ? now.AddDays(pwdDays) : now.AddDays(-pwdDays),
                PasswordExpires = now.AddDays(pwdDays + 42),
                AccountExpires = null,
                ManagerDn = managerDn ?? string.Empty,
                ObjectSid = "S-1-5-21-1000-1000-1000-" + (_users.Count + 1000),
                ObjectGuid = Guid.NewGuid().ToString("D")
            };
            user.Manager = AdValue.DnToName(user.ManagerDn);
            _users.Add(user);
            return user;
        }

        var manager = Add("Волков Дмитрий", "dvolkov", "OU=Sales," + UsersOu, "Руководитель отдела продаж", "Продажи", lastLogonDays: -1);
        manager.Manager = "—";

        var admin1 = Add("Администратор Домена", "admin", "OU=IT," + UsersOu, "Системный администратор", "ИТ", lastLogonDays: -1);
        var admin2 = Add("Иван Админов", "iadminov", "OU=IT," + UsersOu, "Ведущий администратор", "ИТ", lastLogonDays: -210);
        var oldadmin = Add("Старый Админ", "oldadmin", "OU=Disabled," + UsersOu, "Бывший администратор", "ИТ", enabled: false, lastLogonDays: -400);

        var iivanov = Add("Иванов Иван", "iivanov", "OU=IT," + UsersOu, "Инженер", "ИТ", managerDn: admin1.DistinguishedName);
        var ppetrov = Add("Петров Пётр", "ppetrov", "OU=IT," + UsersOu, "Системный администратор", "ИТ", lastLogonDays: -130, managerDn: admin1.DistinguishedName);
        var helpdesk = Add("Сидоров Алексей", "helpdesk1", "OU=IT," + UsersOu, "Специалист поддержки", "ИТ", managerDn: iivanov.DistinguishedName);

        var s1 = Add("Смирнова Анна", "asmirnova", "OU=Sales," + UsersOu, "Менеджер", "Продажи", managerDn: manager.DistinguishedName);
        var s2 = Add("Кузнецов Олег", "okuznetsov", "OU=Sales," + UsersOu, "Менеджер", "Продажи", managerDn: manager.DistinguishedName);
        s2.Locked = true;
        s2.LockoutTime = now.AddHours(-2);
        s2.UserAccountControl |= 0x10;
        var s3 = Add("Морозова Елена", "emorozova", "OU=Sales," + UsersOu, "Менеджер", "Продажи", pwdDays: -95, managerDn: manager.DistinguishedName);
        s3.IsPasswordExpired = true;

        var f1 = Add("Фёдорова Ольга", "ofedorova", "OU=Finance," + UsersOu, "Бухгалтер", "Финансы");
        var f2 = Add("Никитин Павел", "pnikitin", "OU=Finance," + UsersOu, "Экономист", "Финансы");
        f2.AccountExpires = now.AddDays(-3);
        f2.IsAccountExpired = true;

        var disabled1 = Add("Уволенный Сотрудник", "disabled1", "OU=Disabled," + UsersOu, "Бывший менеджер", "Продажи", enabled: false, lastLogonDays: -60);
        var disabled2 = Add("Отключённый Иванов", "disabled2", "OU=Disabled," + UsersOu, "Бывший инженер", "ИТ", enabled: false, lastLogonDays: -300);

        var svcSql = Add("SQL Service", "svc_sql", "OU=ServiceAccounts," + UsersOu, "Сервисная УЗ", "ИТ", lastLogonDays: -1, pwdDays: -500);
        svcSql.PasswordNeverExpires = true;
        svcSql.UserAccountControl |= 0x10000;
        svcSql.HasSpn = true;
        svcSql.Spn.Add("MSSQLSvc/sql01.demo.local:1433");

        var svcBackup = Add("Backup Service", "svc_backup", "OU=ServiceAccounts," + UsersOu, "Сервисная УЗ", "ИТ", lastLogonDays: -2, pwdDays: -700);
        svcBackup.PasswordNeverExpires = true;
        svcBackup.UserAccountControl |= 0x10000;
        svcBackup.DontRequirePreAuth = true;
        svcBackup.TrustedForDelegation = true;
        svcBackup.HasSidHistory = true;
        svcBackup.SidHistory.Add("S-1-5-21-55-66-77-1234");

        var never = Add("Никогда Не Входил", "never1", "OU=IT," + UsersOu, "Стажёр", "ИТ", lastLogonDays: 0, pwdDays: -10);
        never.LastLogon = null;
        never.LastLogonTimestamp = null;
        never.LogonCount = 0;
        never.ManagerDn = string.Empty;

        var noDept = Add("Без Отдела", "nodept", "OU=Users," + UsersOu, "Сотрудник", "", lastLogonDays: -20);
        noDept.ManagerDn = string.Empty;
        noDept.Department = string.Empty;

        var adminProt = Add("Защищённый Админ", "padmin", "OU=IT," + UsersOu, "Администратор", "ИТ", lastLogonDays: -3);
        adminProt.AdminCount = true;
        adminProt.PasswordNeverExpires = true;
        adminProt.UserAccountControl |= 0x10000;

        var delegated = Add("Делегированный Сервис", "delegated", "OU=ServiceAccounts," + UsersOu, "Сервисная УЗ", "ИТ", lastLogonDays: -15);
        delegated.HasConstrainedDelegation = true;
        delegated.AllowedToDelegateTo.Add("HTTP/app01.demo.local");
        delegated.TrustedToAuthForDelegation = true;

        var des = Add("DES Only User", "desuser", "OU=ServiceAccounts," + UsersOu, "Сервисная УЗ", "ИТ", lastLogonDays: -40);
        des.UseDesKeyOnly = true;

        var noPwd = Add("Без Пароля", "nopwd", "OU=IT," + UsersOu, "Стажёр", "ИТ", lastLogonDays: -7);
        noPwd.PasswordNotRequired = true;
        noPwd.UserAccountControl |= 0x20;

        // Сохраняем ссылки для связей групп.
        foreach (var user in _users)
        {
            if (user.PasswordNeverExpires)
            {
                user.PasswordExpires = null;
            }

            user.RefreshDerived();
        }
    }

    private void BuildGroups()
    {
        AdGroup Add(string name, string sam, string description, bool security, bool privileged = false, bool empty = false, bool noOwner = false)
        {
            int groupType = security ? unchecked((int)0x80000000) : 0;
            groupType |= 0x2;
            var group = new AdGroup
            {
                DistinguishedName = $"CN={AdValue.EscapeDnValue(name)},{GroupsOu}",
                Name = name,
                SamAccountName = sam,
                Description = description,
                GroupTypeRaw = groupType,
                GroupTypeText = AdValue.GroupTypeText(groupType),
                GroupScopeText = AdValue.GroupScopeText(groupType),
                IsSecurity = security,
                IsPrivileged = privileged,
                IsEmpty = empty,
                HasNoOwner = noOwner,
                WhenCreated = DateTime.UtcNow.AddDays(-500),
                WhenChanged = DateTime.UtcNow.AddDays(-5),
                ObjectSid = "S-1-5-21-1000-1000-1000-" + (_groups.Count + 2000),
                ObjectGuid = Guid.NewGuid().ToString("D")
            };
            _groups.Add(group);
            return group;
        }

        Add("Domain Admins", "Domain Admins", "Администраторы домена", true, privileged: true);
        var builtinAdmins = Add("Administrators", "Administrators", "Встроенные администраторы (S-1-5-32-544)", true);
        builtinAdmins.ObjectSid = "S-1-5-32-544";
        Add("Enterprise Admins", "Enterprise Admins", "Администраторы леса", true, privileged: true);
        Add("IT-Support", "IT-Support", "Поддержка ИТ", true);
        Add("Sales", "Sales", "Отдел продаж", true);
        Add("Finance", "Finance", "Финансы", true);
        Add("VPN Users", "VPN Users", "Доступ к VPN", true);
        Add("1C-Trade", "1C-Trade", "Доступ к 1С:Торговля", true);
        Add("Legacy Admins", "Legacy Admins", "Старая привилегированная группа", true);
        Add("Protected Users", "Protected Users", "Защищённые пользователи", true, privileged: true);
        Add("Service Accounts", "Service Accounts", "Сервисные учётные записи", true);
        Add("All Staff", "All Staff", "Рассылка для всех сотрудников", false);
        Add("Empty Group", "Empty Group", "Пустая группа", true, empty: true);
        Add("No Owner Group", "No Owner Group", "Группа без владельца", true, noOwner: true);

        AddMembers("Domain Admins", "admin", "iadminov", "IT-Support");
        AddMembers("Administrators", "admin", "padmin", "helpdesk1");
        AddMembers("Enterprise Admins", "admin");
        AddMembers("Protected Users", "admin");
        AddMembers("Legacy Admins", "oldadmin", "Domain Admins");
        AddMembers("IT-Support", "iivanov", "ppetrov", "helpdesk1", "padmin");
        AddMembers("Sales", "dvolkov", "asmirnova", "okuznetsov", "emorozova", "disabled1");
        AddMembers("Finance", "ofedorova", "pnikitin");
        AddMembers("VPN Users", "iivanov", "ppetrov", "asmirnova", "okuznetsov", "emorozova", "ofedorova", "disabled1");
        AddMembers("1C-Trade", "Sales", "Finance");
        AddMembers("Service Accounts", "svc_sql", "svc_backup", "delegated", "desuser");
        AddMembers("All Staff", "iivanov", "ppetrov", "helpdesk1", "asmirnova", "okuznetsov", "emorozova", "ofedorova", "pnikitin");
    }

    private void AddMembers(string groupName, params string[] sams)
    {
        AdGroup? group = _groups.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        if (group is null) return;

        foreach (string sam in sams)
        {
            AdUser? user = _users.FirstOrDefault(u => u.SamAccountName.Equals(sam, StringComparison.OrdinalIgnoreCase));
            if (user is not null)
            {
                group.MemberDns.Add(user.DistinguishedName);
                user.MemberOf.Add(group.DistinguishedName);
            }
            else
            {
                AdGroup? child = _groups.FirstOrDefault(g => g.Name.Equals(sam, StringComparison.OrdinalIgnoreCase));
                if (child is not null)
                {
                    group.MemberDns.Add(child.DistinguishedName);
                    child.MemberOf.Add(group.DistinguishedName);
                }
            }
        }
    }

    private void LinkMembership()
    {
        foreach (var user in _users)
        {
            user.MemberOf = user.MemberOf.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            user.RefreshDerived();
        }

        foreach (var group in _groups)
        {
            group.MemberDns = group.MemberDns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            group.MemberOf = group.MemberOf.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            group.IsEmpty = group.MemberDns.Count == 0;
            group.RefreshDerived();
        }
    }

    public void Dispose()
    {
        IsConnected = false;
    }
}
