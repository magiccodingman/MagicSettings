using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

public sealed class TestSettings
{
    public TestApplication Application { get; set; } = new();
    public TestDatabase Database { get; set; } = new();
    public TestControlPlane MagicSettings { get; set; } = new();
    public List<string> Origins { get; set; } = new();
}
