using Microsoft.Extensions.Hosting;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;
using Openctrol.Agent.Web;
using Openctrol.Agent.RemoteDesktop;

namespace Openctrol.Agent.Hosting;

public sealed class AgentHost : BackgroundService, IUptimeService
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly IControlApiServer? _apiServer;
    private readonly IRemoteDesktopEngine? _remoteDesktopEngine;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public long GetUptimeSeconds() => (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds;
    public DateTimeOffset GetStartTime() => _startTime;

    public AgentHost(
        IConfigManager configManager,
        ILogger logger,
        IControlApiServer? apiServer = null,
        IRemoteDesktopEngine? remoteDesktopEngine = null)
    {
        _configManager = configManager;
        _logger = logger;
        _apiServer = apiServer;
        _remoteDesktopEngine = remoteDesktopEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Openctrol Agent starting...");

        try
        {
            // Validate configuration
            try
            {
                _configManager.ValidateConfig();
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error($"Configuration validation failed: {ex.Message}", ex);
                throw; // Fail startup on invalid config
            }

            var config = _configManager.GetConfig();
            
            // Log configuration summary
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var haIdCount = config.AllowedHaIds?.Count ?? 0;
            var tlsMode = !string.IsNullOrEmpty(config.CertPath) && File.Exists(config.CertPath) ? "HTTPS" : "HTTP";
            
            _logger.Info("=== Openctrol Agent Configuration ===");
            _logger.Info($"Version: {version}");
            _logger.Info($"Agent ID: {config.AgentId}");
            _logger.Info($"Mode: {tlsMode}");
            _logger.Info($"Port: {config.HttpPort}");
            _logger.Info($"Max Sessions: {config.MaxSessions}");
            _logger.Info($"Target FPS: {config.TargetFps}");
            _logger.Info($"HA IDs in allowlist: {haIdCount}");
            _logger.Info($"Audio subsystem: {(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Available" : "Not available")}");
            _logger.Info("=====================================");

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
                try
                {
                    _apiServer.Start();
                    _logger.Info("[API] Server started");
                }
                catch (Exception ex)
                {
                    _logger.Error("[API] Failed to start API server - this is a fatal error", ex);
                    throw; // Fail startup if API server cannot start
                }
            }
            else
            {
                _logger.Warn("[API] Server not available (stub)");
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

