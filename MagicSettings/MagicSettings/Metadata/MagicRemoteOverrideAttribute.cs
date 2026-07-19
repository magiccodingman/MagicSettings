namespace MagicSettings;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicRemoteOverrideAttribute : Attribute
{
    public MagicRemoteOverrideAttribute(bool allowed = true) => Allowed = allowed;
    public bool Allowed { get; }
}
