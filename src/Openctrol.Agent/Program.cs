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
        string? logDirectory = null;
        try
        {
            var eventLogLogger = new EventLogLogger();
            var fileLogger = new FileLogger();
            logDirectory = fileLogger.GetLogDirectory();
            var earlyLoggers = new List<ILogger> { eventLogLogger, fileLogger };
            
            // Add console logger in console mode
            if (isConsoleMode)
            {
                earlyLoggers.Add(new ConsoleLogger());
            }
            
            earlyLogger = new CompositeLogger(earlyLoggers.ToArray());
            
            // Log early boot message
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var mode = isConsoleMode ? "Console" : "Service";
            earlyLogger.Info($"[BOOT] Openctrol Agent starting, version={version}, mode={mode}");
            if (!string.IsNullOrEmpty(logDirectory))
            {
                earlyLogger.Info($"[BOOT] Log directory: {logDirectory}");
            }
        }
        catch (Exception ex)
        {
            // If early logging fails, try Event Log directly
            try
            {
                EventLog.WriteEntry("OpenctrolAgent", 
                    $"[BOOT] Failed to initialize logging: {ex.Message}\nStack: {ex.StackTrace}", 
                    EventLogEntryType.Error);
            }
            catch { }
        }

        IHost? host = null;
        try
        {
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
                    var loggers = new List<ILogger> { eventLogLogger, fileLogger };
                    
                    // Add console logger in console mode
                    if (isConsoleMode)
                    {
                        loggers.Add(new ConsoleLogger());
                    }
                    
                    return new CompositeLogger(loggers.ToArray());
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

            host = hostBuilder.Build();
            
            // Log that host is built
            try
            {
                earlyLogger?.Info("[BOOT] Host built successfully");
            }
            catch { }
            
            // In console mode, add console logging
            if (isConsoleMode)
            {
                try
                {
                    var logger = host.Services.GetRequiredService<ILogger>();
                    logger.Info("[BOOT] Running in console mode - press Ctrl+C to stop");
                }
                catch (Exception ex)
                {
                    try
                    {
                        earlyLogger?.Error($"[BOOT] Failed to get logger from host: {ex.Message}", ex);
                    }
                    catch { }
                }
            }
            
            // Log that we're about to start the host
            try
            {
                earlyLogger?.Info("[BOOT] Starting host (this will start all hosted services)...");
            }
            catch { }
            
            // Start the host - this will start all IHostedService instances
            // Use RunAsync() which internally calls StartAsync and waits for shutdown
            try
            {
                host.RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception runEx)
            {
                try
                {
                    earlyLogger?.Error($"[BOOT] host.RunAsync() threw exception: {runEx.Message}", runEx);
                    EventLog.WriteEntry("OpenctrolAgent", 
                        $"[BOOT] Fatal: host.RunAsync() exception: {runEx.Message}\nType: {runEx.GetType().Name}\nStack: {runEx.StackTrace}", 
                        EventLogEntryType.Error);
                }
                catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            // Log fatal error
            try
            {
                if (earlyLogger != null)
                {
                    earlyLogger.Error($"[BOOT] Fatal error in Program.Main: {ex.Message}", ex);
                }
                else
                {
                    EventLog.WriteEntry("OpenctrolAgent", 
                        $"[BOOT] Fatal error in Program.Main: {ex.Message}\nType: {ex.GetType().Name}\nStack: {ex.StackTrace}", 
                        EventLogEntryType.Error);
                }
            }
            catch { }
            
            // Re-throw to ensure service fails
            throw;
        }
    }
}

