namespace AdManager.Models;

public sealed class AuditEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Operator { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Result { get; set; } = "OK";
    public string? Error { get; set; }
}
