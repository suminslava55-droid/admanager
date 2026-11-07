namespace AdManager.Models;

public sealed class NewGroupRequest
{
    public string ParentDn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSecurity { get; set; } = true;
    public string Scope { get; set; } = "Global"; // Global, DomainLocal, Universal
}
