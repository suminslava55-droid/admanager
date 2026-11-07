using AdManager.Common;

namespace AdManager.Models;

public sealed class KpiItem : ObservableObject
{
    private int _value;
    private bool _isActive;

    public string Label { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string Accent { get; set; } = "#007ACC";
    public int Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}
