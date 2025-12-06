using Microsoft.Extensions.Hosting;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;
using Openctrol.Agent.Web;
using Openctrol.Agent.RemoteDesktop;
using System.Diagnostics;

namespace Openctrol.Agent.Hosting;

public sealed class AgentHost : BackgroundService
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly IControlApiServer _apiServer;
    private readonly IRemoteDesktopEngine? _remoteDesktopEngine;

    public AgentHost(
        IConfigManager configManager,
        ILogger logger,
        IControlApiServer apiServer,
        IRemoteDesktopEngine? remoteDesktopEngine = null)
    {
        // Log constructor call to verify service is being constructed
        try
        {
            System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                "[BOOT] AgentHost constructor called", 
                System.Diagnostics.EventLogEntryType.Information);
        }
        catch { }
        
        _configManager = configManager;
        _logger = logger;
        _apiServer = apiServer;
        _remoteDesktopEngine = remoteDesktopEngine;
        
        // Log that constructor completed
        try
        {
            System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                "[BOOT] AgentHost constructor completed", 
                System.Diagnostics.EventLogEntryType.Information);
        }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield immediately to ensure BackgroundService.StartAsync can return quickly
        await Task.Yield();
        
        // Log entry point - this confirms ExecuteAsync is being called
        try
        {
            System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                "[BOOT] AgentHost.ExecuteAsync called - starting agent initialization", 
                System.Diagnostics.EventLogEntryType.Information);
        }
        catch { }
        
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        _logger.Info($"[BOOT] Openctrol Agent starting, version={version}");
        _logger.Info("[SERVICE] AgentHost.ExecuteAsync entered.");
        _logger.Info("[SERVICE] Starting AgentCore...");

        try
        {
            // Use shared startup logic (includes main loop)
            await AgentCore.StartAsync(
                _configManager,
                _logger,
                _apiServer,
                _remoteDesktopEngine,
                stoppingToken);

            _logger.Info("[SERVICE] AgentCore started successfully (service mode).");
            _logger.Info("[BOOT] Openctrol Agent started successfully");
        }
        catch (Exception ex)
        {
            var errorMsg = $"[BOOT] [ERROR] Fatal error in AgentHost.ExecuteAsync: {ex.Message}";
            _logger.Error(errorMsg, ex);
            
            // Write to Event Log
            try
            {
                System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                    $"{errorMsg}\nType: {ex.GetType().Name}\nStack: {ex.StackTrace}", 
                    System.Diagnostics.EventLogEntryType.Error);
            }
            catch { }
            
            throw; // Re-throw to fail the service
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

