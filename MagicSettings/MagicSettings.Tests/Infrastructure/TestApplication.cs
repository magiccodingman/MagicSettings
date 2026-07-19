using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

public sealed class TestApplication
{
    public string Name { get; set; } = "Test";
    public bool FeatureEnabled { get; set; }
}
