namespace AdManager.Models;

public sealed class AdMember
{
    public string DistinguishedName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string ObjectClass { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public bool IsPrivileged { get; set; }

    public string KindText => IsGroup ? "Группа" : "Пользователь";
    public string DepartmentDisplay => string.IsNullOrWhiteSpace(Department) ? "—" : Department;
    public string TitleDisplay => string.IsNullOrWhiteSpace(Title) ? "—" : Title;
    public string SamDisplay => string.IsNullOrWhiteSpace(SamAccountName) ? "—" : SamAccountName;
    public string PrivilegedText => IsPrivileged ? "да" : "—";
}
