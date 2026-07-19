using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

public sealed class EndpointBootstrapTests
{
    [Fact]
    public void Resolver_UsesEnvironmentBeforePersistentAndFallback()
    {
        const string variable = "MAGICSETTINGS_TEST_CONTROL_PLANE";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "https://environment.example/");
            var options = new MagicSettingsOptions<TestSettings>();
            options.ControlPlane.Bootstrap.EnvironmentVariableName = variable;
            options.ControlPlane.Bootstrap.PersistentSettingPath = "MagicSettings:ControlPlane:Endpoint";
            options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("https://fallback.example/");
            var document = JsonNode.Parse("""{"MagicSettings":{"ControlPlane":{"Endpoint":"https://file.example/"}}}""")!.AsObject();

            var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, document);

            Assert.Equal(MagicControlPlaneEndpointSource.EnvironmentVariable, resolved.Source);
            Assert.Equal("https://environment.example/", resolved.Endpoint!.AbsoluteUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Resolver_UsesPersistentFileWithoutConsultingRemoteState()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.EnvironmentVariableName = "MAGICSETTINGS_TEST_MISSING_ENDPOINT";
        options.ControlPlane.Bootstrap.PersistentSettingPath = "MagicSettings:ControlPlane:Endpoint";
        var local = JsonNode.Parse("""{"MagicSettings":{"ControlPlane":{"Endpoint":"https://local.example/"}}}""")!.AsObject();

        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, local);

        Assert.Equal(MagicControlPlaneEndpointSource.PersistentSettings, resolved.Source);
        Assert.Equal("https://local.example/", resolved.Endpoint!.AbsoluteUri);
    }

    [Fact]
    public void Resolver_RejectsNonLoopbackHttpUnlessExplicitlyAllowed()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("http://control.example/");

        Assert.Throws<InvalidOperationException>(() =>
            new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject()));

        options.ControlPlane.Bootstrap.AllowInsecureHttp = true;
        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject());
        Assert.Equal("http://control.example/", resolved.Endpoint!.AbsoluteUri);
    }

    [Fact]
    public void RuntimeOverride_IsHighestPriority()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("https://fallback.example/");
        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject(), new Uri("https://runtime.example/"));
        Assert.Equal(MagicControlPlaneEndpointSource.RuntimeOverride, resolved.Source);
        Assert.Equal("https://runtime.example/", resolved.Endpoint!.AbsoluteUri);
    }
}
