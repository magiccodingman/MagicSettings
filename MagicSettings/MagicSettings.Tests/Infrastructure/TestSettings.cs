namespace MagicSettings.Tests;

public sealed class TestSettings
{
    public TestApplication Application { get; set; } = new();
    public TestDatabase Database { get; set; } = new();
    public TestControlPlane MagicSettings { get; set; } = new();
    public List<string> Origins { get; set; } = new();
}
