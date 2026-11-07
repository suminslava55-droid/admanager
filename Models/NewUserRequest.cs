namespace AdManager.Models;

public sealed class NewUserRequest
{
    public string ParentDn { get; set; } = string.Empty;
    public string Cn { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string ManagerDn { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Office { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public bool PasswordNeverExpires { get; set; }
    public bool CopyGroups { get; set; } = true;
}
