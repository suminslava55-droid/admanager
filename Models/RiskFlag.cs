namespace AdManager.Models;

public enum RiskSeverity
{
    Critical = 0,
    High = 1,
    Medium = 2,
    Low = 3,
    Info = 4
}

public sealed class RiskFlag
{
    public string Name { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public RiskSeverity Severity { get; set; } = RiskSeverity.Medium;

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

    public override string ToString() => Name;
}
