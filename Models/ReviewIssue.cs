namespace AdManager.Models;

public sealed class ReviewIssue
{
    public RiskSeverity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string SamAccountName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string ObjectDn { get; set; } = string.Empty;
    public AdUser? User { get; set; }
    public AdGroup? Group { get; set; }

    public string SeverityText => Severity switch
    {
        RiskSeverity.Critical => "Критично",
        RiskSeverity.High => "Высокий",
        RiskSeverity.Medium => "Средний",
        RiskSeverity.Low => "Низкий",
        _ => "Инфо"
    };

    public string SeverityColor => Severity switch
    {
        RiskSeverity.Critical => "#F48771",
        RiskSeverity.High => "#F0A35E",
        RiskSeverity.Medium => "#DBCD78",
        RiskSeverity.Low => "#78B4F0",
        _ => "#9696A0"
    };

    public string ObjectType => Group is not null ? "Группа" : "Пользователь";
}
