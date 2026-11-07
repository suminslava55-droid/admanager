using AdManager.Common;

namespace AdManager.Models;

/// <summary>Учётная запись пользователя AD и вычисляемые признаки для access review.</summary>
public sealed class AdUser : ObservableObject
{
    // Идентификация
    public string DistinguishedName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string InitialsRaw { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string ObjectSid { get; set; } = string.Empty;
    public string ObjectGuid { get; set; } = string.Empty;

    // Контакты и организация
    public string Mail { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public string Manager { get; set; } = string.Empty;
    public string ManagerDn { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string IpPhone { get; set; } = string.Empty;
    public string HomePhone { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Office { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeType { get; set; } = string.Empty;

    // Профиль
    public string ProfilePath { get; set; } = string.Empty;
    public string HomeDirectory { get; set; } = string.Empty;
    public string HomeDrive { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string LogonWorkstations { get; set; } = string.Empty;
    public string LogonHours { get; set; } = string.Empty;

    // Почта / Exchange (только чтение в этой версии)
    public string MailNickname { get; set; } = string.Empty;
    public string ProxyAddresses { get; set; } = string.Empty;
    public string TargetAddress { get; set; } = string.Empty;
    public bool HiddenFromGal { get; set; }

    // Учётная запись
    public int UserAccountControl { get; set; }
    public int PrimaryGroupId { get; set; }
    public string PrimaryGroupDn { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
    public bool PasswordNeverExpires { get; set; }
    public bool PasswordNotRequired { get; set; }
    public bool SmartcardRequired { get; set; }
    public bool TrustedForDelegation { get; set; }
    public bool TrustedToAuthForDelegation { get; set; }
    public bool DontRequirePreAuth { get; set; }
    public bool UseDesKeyOnly { get; set; }
    public bool NoAuthDataRequired { get; set; }
    public bool AdminCount { get; set; }
    public bool HasSpn { get; set; }
    public bool HasSidHistory { get; set; }
    public bool HasConstrainedDelegation { get; set; }

    public DateTime? WhenCreated { get; set; }
    public DateTime? WhenChanged { get; set; }
    public DateTime? LastLogon { get; set; }
    public DateTime? LastLogonTimestamp { get; set; }
    public string LastLogonWorkstation { get; set; } = string.Empty;
    public int LogonCount { get; set; }
    public int BadPwdCount { get; set; }
    public DateTime? LastBadPasswordAttempt { get; set; }
    public DateTime? PasswordLastSet { get; set; }
    public DateTime? PasswordExpires { get; set; }
    public DateTime? AccountExpires { get; set; }
    public DateTime? LockoutTime { get; set; }

    public List<string> MemberOf { get; set; } = new();
    public List<string> Spn { get; set; } = new();
    public List<string> AllowedToDelegateTo { get; set; } = new();
    public List<string> SidHistory { get; set; } = new();

    // Результаты access review
    public bool IsPrivileged { get; set; }
    public bool AccountStateReliable { get; set; } = true;
    public List<string> PrivilegedGroups { get; set; } = new();
    public List<string> EffectiveGroups { get; set; } = new();
    public List<RiskFlag> RiskFlags { get; set; } = new();
    public List<ReviewIssue> Issues { get; set; } = new();
    public bool IsStale { get; set; }
    public bool IsNeverLoggedIn { get; set; }
    public bool IsPasswordExpired { get; set; }
    public bool IsAccountExpired { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsDisabledWithAccess { get; set; }
    public int PasswordAgeDays { get; set; }
    public int DaysInactive { get; set; }
    public int DaysToExpiry { get; set; }

    // Детали / сырые атрибуты
    public Dictionary<string, string> AllAttributes { get; set; } = new();
    public List<AttributeItem> AllAttributeItems { get; set; } = new();
    public bool DetailsLoaded { get; set; }

    public string DisplayLabel => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName
        : !string.IsNullOrWhiteSpace(Name) ? Name : SamAccountName;

    public string Initials => AdValue.Initials(DisplayLabel, SamAccountName);
    public string OuPath => AdValue.DnToOu(DistinguishedName);
    public string OuDisplay => string.IsNullOrWhiteSpace(OuPath) ? "—" : OuPath;
    public string ManagerDisplay => string.IsNullOrWhiteSpace(Manager) ? "—" : Manager;
    public string MailDisplay => string.IsNullOrWhiteSpace(Mail) ? "—" : Mail;
    public string DepartmentDisplay => string.IsNullOrWhiteSpace(Department) ? "—" : Department;
    public string TitleDisplay => string.IsNullOrWhiteSpace(Title) ? "—" : Title;
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
    public string MobileDisplay => string.IsNullOrWhiteSpace(Mobile) ? "—" : Mobile;
    public string OfficeDisplay => string.IsNullOrWhiteSpace(Office) ? "—" : Office;
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description;
    public string UpnDisplay => string.IsNullOrWhiteSpace(UserPrincipalName) ? "—" : UserPrincipalName;
    public string SamDisplay => string.IsNullOrWhiteSpace(SamAccountName) ? "—" : SamAccountName;

    public string StatusText
    {
        get
        {
            if (!Enabled) return "Отключена";
            if (!AccountStateReliable) return "Статус н/д";
            if (Locked) return "Заблокирована";
            if (IsAccountExpired) return "Срок истёк";
            if (IsPasswordExpired) return "Пароль истёк";
            if (MustChangePassword) return "Сменить пароль";
            return "Активна";
        }
    }

    public string StatusColor
    {
        get
        {
            if (!Enabled) return "#9696A0";
            if (!AccountStateReliable) return "#9696A0";
            if (Locked) return "#F48771";
            if (IsAccountExpired) return "#F48771";
            if (IsPasswordExpired) return "#F0A35E";
            if (MustChangePassword) return "#DBCD78";
            return "#89D185";
        }
    }

    public string PasswordLastSetText => AdValue.ToLocalText(PasswordLastSet);
    public string PasswordExpiresText => PasswordNeverExpires ? "никогда" : AdValue.ToLocalText(PasswordExpires);
    public string AccountExpiresText => AccountExpires.HasValue ? AdValue.ToLocalText(AccountExpires) : "никогда";
    public string LastLogonText
    {
        get
        {
            if (LastLogon.HasValue) return AdValue.ToLocalText(LastLogon);
            if (LastLogonTimestamp.HasValue) return "≈ " + AdValue.ToLocalText(LastLogonTimestamp);
            return "никогда";
        }
    }

    public string LastLogonWorkstationText =>
        string.IsNullOrWhiteSpace(LastLogonWorkstation) ? "не определён" : LastLogonWorkstation;

    public string WhenCreatedText => AdValue.ToLocalText(WhenCreated);
    public string WhenChangedText => AdValue.ToLocalText(WhenChanged);

    public string PasswordAgeText => PasswordLastSet.HasValue ? $"{PasswordAgeDays} дн." : "—";
    public string DaysInactiveText => IsNeverLoggedIn ? "никогда" : $"{DaysInactive} дн.";
    public string DaysToExpiryText => !AccountExpires.HasValue ? "никогда" : DaysToExpiry < 0 ? "истёк" : $"{DaysToExpiry} дн.";
    public string GroupsCountText => MemberOf.Count == 0 ? "0" : MemberOf.Count.ToString();

    public string MemberOfNames => MemberOf.Count == 0
        ? "Нет групп"
        : string.Join(Environment.NewLine, MemberOf.Select(AdValue.DnToName).OrderBy(x => x));

    public string PrivilegedGroupsText => PrivilegedGroups.Count == 0
        ? "Нет привилегированных групп"
        : string.Join(Environment.NewLine, PrivilegedGroups.OrderBy(x => x));

    public string EffectiveGroupsText => EffectiveGroups.Count == 0
        ? "Эффективные группы не загружены"
        : string.Join(Environment.NewLine, EffectiveGroups.OrderBy(x => x));

    public string RiskShort
    {
        get
        {
            if (RiskFlags.Count == 0) return string.Empty;
            var names = RiskFlags.OrderBy(f => f.Severity).Select(f => f.Name).ToList();
            return names.Count <= 3
                ? string.Join(" · ", names)
                : string.Join(" · ", names.Take(3)) + $" +{names.Count - 3}";
        }
    }

    public string RiskCountText => RiskFlags.Count == 0 ? "нет" : RiskFlags.Count.ToString();
    public bool HasRiskFlags => RiskFlags.Count > 0;
    public bool HasNoManager => string.IsNullOrWhiteSpace(ManagerDn);
    public bool HasNoDepartment => string.IsNullOrWhiteSpace(Department);
    public bool HasNoMail => string.IsNullOrWhiteSpace(Mail);

    public string SearchBlob => string.Join(" ",
        DisplayLabel, Name, SamAccountName, UserPrincipalName, Mail, Department, Title,
        Description, EmployeeId, OuPath).ToLowerInvariant();

    public void RefreshDerived()
    {
        RaiseAll();
    }
}
