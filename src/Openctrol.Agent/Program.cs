using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;
using Openctrol.Agent.Logging;
using Openctrol.Agent.Hosting;
using Openctrol.Agent.Web;
using Openctrol.Agent.Security;
using Openctrol.Agent.SystemState;
using Openctrol.Agent.RemoteDesktop;
using Openctrol.Agent.Input;
using Openctrol.Agent.Power;
using Openctrol.Agent.Audio;
using System.Diagnostics;

namespace Openctrol.Agent;

public static class Program
{
    public static void Main(string[] args)
    {
        // Check for console mode
        var isConsoleMode = args.Contains("--console") || Environment.UserInteractive;
        
        // Initialize early logging (before host builder)
        ILogger? earlyLogger = null;
        try
        {
            var eventLogLogger = new EventLogLogger();
            var fileLogger = new FileLogger();
            earlyLogger = new CompositeLogger(eventLogLogger, fileLogger);
            
            // Log early boot message
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var mode = isConsoleMode ? "Console" : "Service";
            earlyLogger.Info($"[BOOT] Openctrol Agent starting, version={version}, mode={mode}");
        }
        catch (Exception ex)
        {
            // If early logging fails, try Event Log directly
            try
            {
                EventLog.WriteEntry("OpenctrolAgent", 
                    $"[BOOT] Failed to initialize logging: {ex.Message}", 
                    EventLogEntryType.Error);
            }
            catch { }
        }

        var hostBuilder = Host.CreateDefaultBuilder(args);
        
        // Only use Windows Service if not in console mode
        if (!isConsoleMode)
        {
            hostBuilder.UseWindowsService();
        }
        
        hostBuilder.ConfigureServices((ctx, services) =>
        {
            services.AddSingleton<IConfigManager, JsonConfigManager>();
            services.AddSingleton<ILogger, CompositeLogger>(sp =>
            {
                var eventLogLogger = new EventLogLogger();
                var fileLogger = new FileLogger();
                return new CompositeLogger(eventLogLogger, fileLogger);
            });
            
            services.AddSingleton<InputDispatcher>();
            services.AddSingleton<ISecurityManager, SecurityManager>();
            services.AddSingleton<ISystemStateMonitor, SystemStateMonitor>();
            services.AddSingleton<IRemoteDesktopEngine, RemoteDesktopEngine>();
            services.AddSingleton<ISessionBroker, SessionBroker>();
            services.AddSingleton<IPowerManager, PowerManager>();
            services.AddSingleton<IAudioManager, AudioManager>();
            services.AddSingleton<IControlApiServer, ControlApiServer>();

            // Register AgentHost as both hosted service and uptime service
            services.AddSingleton<AgentHost>();
            services.AddSingleton<IUptimeService>(sp => sp.GetRequiredService<AgentHost>());
            services.AddHostedService<AgentHost>(sp => sp.GetRequiredService<AgentHost>());
        });

        var host = hostBuilder.Build();
        
        // In console mode, add console logging
        if (isConsoleMode)
        {
            var logger = host.Services.GetRequiredService<ILogger>();
            logger.Info("[BOOT] Running in console mode - press Ctrl+C to stop");
        }
        
        host.Run();
    }
}

