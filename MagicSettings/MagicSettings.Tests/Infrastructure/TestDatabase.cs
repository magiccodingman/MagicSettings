using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

public sealed class TestDatabase
{
    public string Host { get; set; } = "localhost";
    public string Password { get; set; } = "change-me";
    public int Port { get; set; } = 5432;
}
