using AdManager.Common;

namespace AdManager.Models;

public sealed class AdGroup : ObservableObject
{
    public string DistinguishedName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ObjectSid { get; set; } = string.Empty;
    public string ObjectGuid { get; set; } = string.Empty;
    public int GroupTypeRaw { get; set; }
    public string GroupTypeText { get; set; } = string.Empty;
    public string GroupScopeText { get; set; } = string.Empty;
    public bool IsSecurity { get; set; }
    public bool IsPrivileged { get; set; }
    public bool IsEmpty { get; set; }
    public bool HasNoOwner { get; set; }
    public bool IsNestedPrivileged { get; set; }
    public string ManagedBy { get; set; } = string.Empty;
    public string ManagedByDn { get; set; } = string.Empty;
    public DateTime? WhenCreated { get; set; }
    public DateTime? WhenChanged { get; set; }

    public List<string> MemberDns { get; set; } = new();
    public List<string> MemberOf { get; set; } = new();
    public List<AdMember> Members { get; set; } = new();
    public List<RiskFlag> RiskFlags { get; set; } = new();
    public List<ReviewIssue> Issues { get; set; } = new();
    public Dictionary<string, string> AllAttributes { get; set; } = new();
    public List<AttributeItem> AllAttributeItems { get; set; } = new();
    public bool DetailsLoaded { get; set; }

    public int MemberCount => MemberDns.Count;
    public string MemberCountText => MemberDns.Count.ToString();
    public string OuPath => AdValue.DnToOu(DistinguishedName);
    public string OuDisplay => string.IsNullOrWhiteSpace(OuPath) ? "—" : OuPath;
    public string ManagedByDisplay => string.IsNullOrWhiteSpace(ManagedBy) ? "—" : ManagedBy;
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description;
    public string WhenCreatedText => AdValue.ToLocalText(WhenCreated);
    public string WhenChangedText => AdValue.ToLocalText(WhenChanged);

    public string StatusText
    {
        get
        {
            if (IsPrivileged) return "Привилегированная";
            if (IsEmpty) return "Пустая";
            if (HasNoOwner) return "Без владельца";
            return IsSecurity ? "Группа безопасности" : "Рассылка";
        }
    }

    public string StatusColor
    {
        get
        {
            if (IsPrivileged) return "#F0A35E";
            if (IsEmpty) return "#9696A0";
            if (HasNoOwner) return "#DBCD78";
            return IsSecurity ? "#89D185" : "#78B4F0";
        }
    }

    public string RiskShort
    {
        get
        {
            if (RiskFlags.Count == 0) return string.Empty;
            return string.Join(" · ", RiskFlags.OrderBy(f => f.Severity).Select(f => f.Name));
        }
    }

    public string MemberOfNames => MemberOf.Count == 0
        ? "Не входит в другие группы"
        : string.Join(Environment.NewLine, MemberOf.Select(AdValue.DnToName).OrderBy(x => x));

    public string SearchBlob => string.Join(" ",
        Name, SamAccountName, Description, OuPath).ToLowerInvariant();

    public void RefreshDerived() => RaiseAll();
}
