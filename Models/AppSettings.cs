namespace AdManager.Models;

public sealed class AppSettings
{
    public string Domain { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public bool UseDemo { get; set; }
    public bool IncludeDisabled { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public int StaleDays { get; set; } = 90;
    public int PasswordExpiringDays { get; set; } = 14;
    public int PageSize { get; set; } = 500;
    public int MaxObjects { get; set; } = 20000;
    public int PasswordLength { get; set; } = 14;
    public string LastOuDn { get; set; } = string.Empty;
    public List<string> EnabledProblems { get; set; } = ProblemCatalog.All.ToList();
    public List<string> PrivilegedGroups { get; set; } = new()
    {
        "Domain Admins",
        "Enterprise Admins",
        "Schema Admins",
        "Administrators",
        "Account Operators",
        "Backup Operators",
        "Print Operators",
        "Server Operators",
        "Group Policy Creator Owners",
        "Replicator",
        "Read-only Domain Controllers",
        "DnsAdmins",
        "Key Admins",
        "Enterprise Key Admins",
        "Protected Users",
        "Exchange Organization Management",
        "Exchange Recipient Management"
    };

    public double WindowWidth { get; set; } = 1600;
    public double WindowHeight { get; set; } = 920;

    public AppSettings Clone() => new()
    {
        Domain = Domain,
        Server = Server,
        UseDemo = UseDemo,
        IncludeDisabled = IncludeDisabled,
        Recursive = Recursive,
        StaleDays = StaleDays,
        PasswordExpiringDays = PasswordExpiringDays,
        PageSize = PageSize,
        MaxObjects = MaxObjects,
        PasswordLength = PasswordLength,
        LastOuDn = LastOuDn,
        EnabledProblems = new List<string>(EnabledProblems),
        PrivilegedGroups = new List<string>(PrivilegedGroups),
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight
    };
}
