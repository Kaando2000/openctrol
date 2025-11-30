using Microsoft.Extensions.Hosting;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;
using Openctrol.Agent.Web;
using Openctrol.Agent.RemoteDesktop;
using System.IO;

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
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        _logger.Info($"[BOOT] Openctrol Agent starting, version={version}");

        try
        {
            // Load and validate configuration
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Openctrol",
                "config.json");
            _logger.Info($"[BOOT] Config loaded from {configPath}");
            
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
            
            // Log configuration summary with [BOOT] and [CONFIG] prefixes
            var haIdCount = config.AllowedHaIds?.Count ?? 0;
            var tlsMode = !string.IsNullOrEmpty(config.CertPath) && File.Exists(config.CertPath) ? "HTTPS" : "HTTP";
            var authMode = string.IsNullOrEmpty(config.ApiKey) ? "DISABLED (development mode)" : "ENABLED";
            
            _logger.Info($"[CONFIG] HTTP port={config.HttpPort}, UseHttps={tlsMode == "HTTPS"}, CertPath={config.CertPath ?? "<none>"}");
            _logger.Info("=== Openctrol Agent Configuration ===");
            _logger.Info($"Version: {version}");
            _logger.Info($"Agent ID: {config.AgentId}");
            _logger.Info($"Mode: {tlsMode}");
            _logger.Info($"Port: {config.HttpPort}");
            _logger.Info($"Max Sessions: {config.MaxSessions}");
            _logger.Info($"Target FPS: {config.TargetFps}");
            _logger.Info($"HA IDs in allowlist: {haIdCount}");
            _logger.Info($"Authentication: {authMode}");
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                _logger.Warn("SECURITY WARNING: API key not configured. REST endpoints are accessible without authentication!");
            }
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
                    _logger.Info("[BOOT] Starting HTTP API server...");
                    
                    // Write directly to Event Log before starting
                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                            "[BOOT] Starting HTTP API server...", 
                            System.Diagnostics.EventLogEntryType.Information);
                    }
                    catch { }
                    
                    _apiServer.Start();
                    
                    // Success message is logged by ControlApiServer.Start()
                }
                catch (Exception ex)
                {
                    var errorMsg = $"[API] Failed to start API server - this is a fatal error: {ex.Message}";
                    _logger.Error(errorMsg, ex);
                    
                    // Write directly to Event Log
                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                            $"{errorMsg}\nType: {ex.GetType().Name}\nStack: {ex.StackTrace}", 
                            System.Diagnostics.EventLogEntryType.Error);
                    }
                    catch { }
                    
                    throw; // Fail startup if API server cannot start
                }
            }
            else
            {
                _logger.Warn("[API] Server not available (stub)");
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                        "[API] WARNING: Server not available (stub)", 
                        System.Diagnostics.EventLogEntryType.Warning);
                }
                catch { }
            }

            _logger.Info("[BOOT] Openctrol Agent started successfully");

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

