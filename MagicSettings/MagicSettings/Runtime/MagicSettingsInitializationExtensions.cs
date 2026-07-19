using System.Text.Json.Nodes;
using MagicSettings.Share;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace MagicSettings;

public static class MagicSettingsInitializationExtensions
{
    public static async ValueTask<MagicSettingsInitializationResult> AddMagicSettingsAsync<TSettings>(
        this IHostApplicationBuilder builder,
        string[]? args = null,
        Action<MagicSettingsOptions<TSettings>>? configure = null,
        CancellationToken cancellationToken = default)
        where TSettings : class, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new MagicSettingsOptions<TSettings>();
        configure?.Invoke(options);
        options.Environment = MagicSettingsEnvironmentResolver.Resolve(options.Environment, builder.Environment.EnvironmentName);
        if (options.EnvironmentProfiles.TryGetValue(options.Environment, out var environmentProfile))
        {
            environmentProfile(options.Template);
        }

        var command = MagicSettingsCommandLine.Parse(args ?? Array.Empty<string>());
        var path = MagicSettingsPathResolver.Resolve(options);
        var documentResult = await MagicSettingsDocumentStore.LoadAndReconcileAsync(
            path,
            options,
            command.ForceGenerate,
            cancellationToken);

        if (command.PrintPath)
        {
            Console.Out.WriteLine(path);
        }

        var identityPath = MagicIdentityPathResolver.Resolve(path, options.IdentityPath, options.IdentityFileName);
        var identityStore = options.IdentityStore ?? new FileMagicNodeIdentityStore(identityPath);
        var identityManager = new MagicNodeIdentityManager(identityStore);
        await identityManager.GetCurrentAsync(cancellationToken);

        var configurationProvider = new MagicSettingsConfigurationProvider();
        var runtime = new MagicSettingsRuntime<TSettings>(options, configurationProvider, path);
        await runtime.InitializeAsync(documentResult.Document, cancellationToken);

        var endpointResolver = options.ControlPlaneEndpointResolver ?? new MagicControlPlaneEndpointResolver();
        var transport = options.ControlPlaneTransport ?? new HttpMagicControlPlaneTransport();
        var controlPlane = new MagicSettingsControlPlane<TSettings>(
            options,
            runtime,
            identityManager,
            identityManager,
            transport,
            endpointResolver,
            documentResult.Document,
            documentResult.MigrationReport);

        builder.Configuration.Add(new MagicSettingsConfigurationSource(configurationProvider));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMagicSettings<TSettings>>(runtime);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<IMagicNodeIdentityManager>(identityManager);
        builder.Services.AddSingleton<IMagicNodeAuthenticator>(identityManager);
        builder.Services.AddSingleton<IMagicSettingsControlPlane>(controlPlane);
        builder.Services.AddSingleton(controlPlane);
        builder.Services.AddSingleton<IMagicControlPlaneEndpointResolver>(endpointResolver);
        builder.Services.AddSingleton<IMagicControlPlaneTransport>(transport);
        if (transport is IMagicSecretTransport secretTransport)
        {
            builder.Services.AddSingleton<IMagicSecretTransport>(secretTransport);
            builder.Services.AddSingleton<IMagicSecretProvider>(
                new MagicControlPlaneSecretProvider<TSettings>(controlPlane, identityManager, secretTransport, options));
        }
        builder.Services.AddSingleton(new MagicSettingsRuntimeRegistration(path));
        builder.Services.AddHostedService(serviceProvider =>
            new MagicSettingsHostedService<TSettings>(
                options,
                runtime,
                controlPlane,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MagicSettingsHostedService<TSettings>>>(),
                path));
        builder.Services.AddOptions<TSettings>().Bind(builder.Configuration);

        var shouldExit = command.GenerateOnly || command.ValidateOnly || command.PrintPath;
        return new(shouldExit, 0, path, options.Environment);
    }
}
