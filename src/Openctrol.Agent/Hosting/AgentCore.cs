using System;
using System.Threading;
using System.Threading.Tasks;
using Openctrol.Agent.Config;
using Openctrol.Agent.Web;
using Openctrol.Agent.RemoteDesktop;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Hosting;

/// <summary>
/// Shared core startup logic used by both console mode (AgentBootstrap) and service mode (AgentHost).
/// </summary>
internal static class AgentCore
{
    public static async Task StartAsync(
        IConfigManager configManager,
        ILogger logger,
        IControlApiServer apiServer,
        IRemoteDesktopEngine? remoteDesktopEngine,
        CancellationToken cancellationToken)
    {
        // 1) Validate configuration
        logger.Info("Validating configuration...");
        configManager.ValidateConfig();
        logger.Info("Configuration valid.");

        // 2) Start remote desktop engine (optional)
        if (remoteDesktopEngine != null)
        {
            logger.Info("Starting remote desktop engine...");
            remoteDesktopEngine.Start();
            logger.Info("Remote desktop engine started.");
        }
        else
        {
            logger.Warn("No remote desktop engine registered; continuing without it.");
        }

        // 3) Start HTTP API server
        logger.Info("Starting HTTP API server...");
        await apiServer.StartAsync(cancellationToken);
        logger.Info("HTTP API server started successfully.");

        // 4) Main loop - keep running until cancellation
        logger.Info("Agent core is now running.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        
        logger.Info("Agent core stopping (cancellation requested).");
    }
}

