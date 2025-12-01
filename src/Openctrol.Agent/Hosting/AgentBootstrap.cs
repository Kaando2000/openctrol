using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Openctrol.Agent.Config;
using Openctrol.Agent.Web;
using Openctrol.Agent.RemoteDesktop;
using Openctrol.Agent.SystemState;
using Openctrol.Agent.Audio;
using Openctrol.Agent.Security;
using Openctrol.Agent.Power;

namespace Openctrol.Agent.Hosting;

public static class AgentBootstrap
{
    public static async Task<int> RunConsoleAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("AgentBootstrap.RunConsoleAsync starting.");

        try
        {
            logger.LogInformation("Getting IConfigManager from service provider...");
            var configManager = services.GetRequiredService<IConfigManager>();
            logger.LogInformation("IConfigManager obtained successfully.");
            
            logger.LogInformation("Getting IControlApiServer dependencies individually...");
            IControlApiServer apiServer;
            try
            {
                logger.LogInformation("  - Getting ILogger...");
                var loggerService = services.GetRequiredService<Openctrol.Agent.Logging.ILogger>();
                logger.LogInformation("  - ILogger obtained.");
                
                logger.LogInformation("  - Getting ISecurityManager...");
                var securityManager = services.GetService<ISecurityManager>();
                logger.LogInformation("  - ISecurityManager obtained (may be null).");
                
                logger.LogInformation("  - Getting ISessionBroker...");
                var sessionBroker = services.GetService<ISessionBroker>();
                logger.LogInformation("  - ISessionBroker obtained (may be null).");
                
                logger.LogInformation("  - Getting IRemoteDesktopEngine...");
                var rdEngineForApi = services.GetService<IRemoteDesktopEngine>();
                logger.LogInformation("  - IRemoteDesktopEngine obtained (may be null).");
                
                logger.LogInformation("  - Getting IPowerManager...");
                var powerManager = services.GetService<IPowerManager>();
                logger.LogInformation("  - IPowerManager obtained (may be null).");
                
                logger.LogInformation("  - Getting IAudioManager...");
                var audioManager = services.GetService<IAudioManager>();
                logger.LogInformation("  - IAudioManager obtained (may be null).");
                
                // IUptimeService is now independent (UptimeService), no circular dependency
                logger.LogInformation("  - IUptimeService is registered independently (UptimeService).");
                
                logger.LogInformation("  - Getting ISystemStateMonitor...");
                var systemStateMonitor = services.GetService<ISystemStateMonitor>();
                logger.LogInformation("  - ISystemStateMonitor obtained (may be null).");
                
                logger.LogInformation("Getting IControlApiServer from service provider...");
                apiServer = services.GetRequiredService<IControlApiServer>();
                logger.LogInformation("IControlApiServer obtained successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get service from service provider.");
                throw;
            }
            
            logger.LogInformation("Getting IRemoteDesktopEngine from service provider...");
            var rdEngine = services.GetService<IRemoteDesktopEngine>();
            logger.LogInformation("IRemoteDesktopEngine obtained (may be null).");

            // Get custom logger for AgentCore
            var customLogger = services.GetRequiredService<Openctrol.Agent.Logging.ILogger>();
            
            // Use shared startup logic (includes main loop)
            await AgentCore.StartAsync(
                configManager,
                customLogger,
                apiServer,
                rdEngine,
                cancellationToken);

            logger.LogInformation("AgentBootstrap.RunConsoleAsync stopping (cancellation requested).");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error in AgentBootstrap.RunConsoleAsync.");
            return 1;
        }
    }
}

