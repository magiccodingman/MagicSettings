namespace MagicSettings.Tests;

public sealed class TestDatabase
{
    public string Host { get; set; } = "localhost";
    public string Password { get; set; } = "change-me";
    public int Port { get; set; } = 5432;
}
