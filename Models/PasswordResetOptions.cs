namespace AdManager.Models;

public sealed class PasswordResetOptions
{
    public string Password { get; set; } = string.Empty;
    public bool MustChange { get; set; } = true;
    public bool Unlock { get; set; }
}
