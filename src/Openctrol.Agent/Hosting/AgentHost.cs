using Microsoft.Extensions.Hosting;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;
using Openctrol.Agent.Web;
using Openctrol.Agent.Discovery;
using Openctrol.Agent.RemoteDesktop;

namespace Openctrol.Agent.Hosting;

public sealed class AgentHost : BackgroundService
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly IControlApiServer? _apiServer;
    private readonly IDiscoveryBroadcaster? _discovery;
    private readonly IRemoteDesktopEngine? _remoteDesktopEngine;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public long UptimeSeconds => (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds;

    public AgentHost(
        IConfigManager configManager,
        ILogger logger,
        IControlApiServer? apiServer = null,
        IDiscoveryBroadcaster? discovery = null,
        IRemoteDesktopEngine? remoteDesktopEngine = null)
    {
        _configManager = configManager;
        _logger = logger;
        _apiServer = apiServer;
        _discovery = discovery;
        _remoteDesktopEngine = remoteDesktopEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Openctrol Agent starting...");

        try
        {
            var config = _configManager.GetConfig();
            _logger.Info($"Agent ID: {config.AgentId}");
            _logger.Info($"HTTP Port: {config.HttpPort}");

            // Start remote desktop engine if available
            if (_remoteDesktopEngine != null)
            {
                _remoteDesktopEngine.Start();
                _logger.Info("Remote desktop engine started");
            }
            else
            {
                _logger.Warn("Remote desktop engine not available (stub)");
            }

            // Start API server if available
            if (_apiServer != null)
            {
                _apiServer.Start();
                _logger.Info("API server started");
            }
            else
            {
                _logger.Warn("API server not available (stub)");
            }

            // Start discovery if available
            if (_discovery != null)
            {
                _discovery.Start();
                _logger.Info("Discovery broadcaster started");
            }
            else
            {
                _logger.Warn("Discovery broadcaster not available (stub)");
            }

            _logger.Info("Openctrol Agent started successfully");

            // Keep running until cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error in AgentHost", ex);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Openctrol Agent stopping...");

        if (_discovery != null)
        {
            _discovery.Stop();
            _logger.Info("Discovery broadcaster stopped");
        }

        if (_apiServer != null)
        {
            await _apiServer.StopAsync();
            _logger.Info("API server stopped");
        }

        if (_remoteDesktopEngine != null)
        {
            _remoteDesktopEngine.Stop();
            _logger.Info("Remote desktop engine stopped");
        }

        _logger.Info("Openctrol Agent stopped");
        await base.StopAsync(cancellationToken);
    }
}

