using AdManager.Common;
using AdManager.Models;

namespace AdManager.Dialogs;

public sealed class OuOption
{
    public AdOu Ou { get; init; } = new();
    public string Display
    {
        get
        {
            string path = AdValue.DnToOu(Ou.DistinguishedName);
            return string.IsNullOrWhiteSpace(path) ? Ou.DistinguishedName : path;
        }
    }

    public override string ToString() => Display;
}
