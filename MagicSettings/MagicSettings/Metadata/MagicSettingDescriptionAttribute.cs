namespace MagicSettings;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicSettingDescriptionAttribute : Attribute
{
    public MagicSettingDescriptionAttribute(string description) => Description = description;
    public string Description { get; }
}
