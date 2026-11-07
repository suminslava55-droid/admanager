using System.Collections.ObjectModel;
using AdManager.Common;

namespace AdManager.Models;

public sealed class AdOu : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string DistinguishedName { get; set; } = string.Empty;
    public string ParentDn { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsExpanded { get; set; }
    public ObservableCollection<AdOu> Children { get; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DistinguishedName : Name;
    public override string ToString() => DisplayName;
}
