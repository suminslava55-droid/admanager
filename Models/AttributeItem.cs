namespace AdManager.Models;

public sealed class AttributeItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public AttributeItem()
    {
    }

    public AttributeItem(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
