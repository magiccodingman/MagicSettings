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

internal sealed class MagicSettingsHostedService<TSettings> : BackgroundService where TSettings : class, new()
{
    private readonly MagicSettingsOptions<TSettings> _options;
    private readonly MagicSettingsRuntime<TSettings> _runtime;
    private readonly MagicSettingsControlPlane<TSettings> _controlPlane;
    private readonly ILogger<MagicSettingsHostedService<TSettings>> _logger;
    private readonly string _settingsPath;
    private DateTime _lastWriteUtc;
    private DateTimeOffset _nextRemotePoll;

    public MagicSettingsHostedService(
        MagicSettingsOptions<TSettings> options,
        MagicSettingsRuntime<TSettings> runtime,
        MagicSettingsControlPlane<TSettings> controlPlane,
        ILogger<MagicSettingsHostedService<TSettings>> logger,
        string settingsPath)
    {
        _options = options;
        _runtime = runtime;
        _controlPlane = controlPlane;
        _logger = logger;
        _settingsPath = settingsPath;
        _lastWriteUtc = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath) : DateTime.MinValue;
        _nextRemotePoll = DateTimeOffset.UtcNow.Add(options.ControlPlane.PollInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _controlPlane.BootstrapAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MagicSettings could not establish the bootstrap control-plane connection. Local configuration remains active.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _runtime.RefreshRemoteExpirations(DateTimeOffset.UtcNow);
            if (_options.ReloadOnChange)
            {
                await CheckPersistentFileAsync(stoppingToken);
            }

            if (DateTimeOffset.UtcNow >= _nextRemotePoll)
            {
                await PollRemoteAsync(stoppingToken);
                _nextRemotePoll = DateTimeOffset.UtcNow.Add(JitteredPollInterval());
            }
        }
    }

    private async ValueTask CheckPersistentFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var currentWrite = File.GetLastWriteTimeUtc(_settingsPath);
        if (currentWrite <= _lastWriteUtc)
        {
            return;
        }

        await Task.Delay(_options.ReloadDebounce, cancellationToken);
        try
        {
            await _runtime.ReloadPersistentAsync(cancellationToken);
            var document = await MagicSettingsDocumentStore.ReadAsync(_settingsPath, cancellationToken);
            await _controlPlane.ReevaluatePersistentEndpointAsync(document, cancellationToken);
            _lastWriteUtc = currentWrite;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MagicSettings rejected an invalid runtime reload and retained the last known good snapshot.");
        }
    }

    private async ValueTask PollRemoteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _controlPlane.RefreshAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "MagicSettings could not refresh remote overrides. The last known good remote snapshot remains active.");
        }
    }

    private TimeSpan JitteredPollInterval()
    {
        var jitter = _options.ControlPlane.PollJitter;
        if (jitter <= TimeSpan.Zero)
        {
            return _options.ControlPlane.PollInterval;
        }

        var milliseconds = Random.Shared.NextDouble() * jitter.TotalMilliseconds * 2 - jitter.TotalMilliseconds;
        return _options.ControlPlane.PollInterval + TimeSpan.FromMilliseconds(milliseconds);
    }
}
