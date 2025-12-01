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
using System.Threading;

namespace Openctrol.Agent;

public static class Program
{
    public static int Main(string[] args)
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
                
                // Register IUptimeService first (independent, no dependencies)
                services.AddSingleton<IUptimeService, UptimeService>();
                
                services.AddSingleton<InputDispatcher>();
                services.AddSingleton<ISecurityManager, SecurityManager>();
                services.AddSingleton<ISystemStateMonitor, SystemStateMonitor>();
                services.AddSingleton<IRemoteDesktopEngine, RemoteDesktopEngine>();
                services.AddSingleton<ISessionBroker, SessionBroker>();
                services.AddSingleton<IPowerManager, PowerManager>();
                services.AddSingleton<IAudioManager, AudioManager>();
                
                // Register IControlApiServer (now depends on IUptimeService, not AgentHost)
                services.AddSingleton<IControlApiServer, ControlApiServer>();

                // Register AgentHost as hosted service (only in service mode)
                if (!isConsoleMode)
                {
                    services.AddSingleton<AgentHost>();
                    services.AddHostedService<AgentHost>(sp => sp.GetRequiredService<AgentHost>());
                }
            });

            using var host = hostBuilder.Build();
            
            // Log that host is built
            try
            {
                earlyLogger?.Info("[BOOT] Host built successfully");
            }
            catch { }
            
            var services = host.Services;
            
            if (isConsoleMode)
            {
                // CONSOLE MODE: do NOT call host.StartAsync / RunAsync at all.
                earlyLogger?.Info("[BOOT] Running in console mode via AgentBootstrap - press Ctrl+C to stop");
                
                try
                {
                    // Get our custom logger and wrap it for AgentBootstrap
                    var customLogger = services.GetRequiredService<ILogger>();
                    var loggerAdapter = new LoggerAdapter(customLogger);
                    
                    using var cts = new CancellationTokenSource();
                    Console.CancelKeyPress += (_, e) =>
                    {
                        e.Cancel = true;
                        cts.Cancel();
                    };
                    
                    var exitCode = AgentBootstrap.RunConsoleAsync(services, loggerAdapter, cts.Token)
                        .GetAwaiter()
                        .GetResult();
                    
                    return exitCode;
                }
                catch (Exception ex)
                {
                    earlyLogger?.Error("[BOOT] Fatal exception in AgentBootstrap console run.", ex);
                    throw;
                }
            }
            else
            {
                // SERVICE MODE: keep existing Windows service hosting behavior
                earlyLogger?.Info("[BOOT] Running as Windows service");
                earlyLogger?.Info("[BOOT] Service mode: host.RunAsync is about to be called.");
                
                try
                {
                    earlyLogger?.Info("[BOOT] Calling host.RunAsync()...");
                    var runTask = host.RunAsync();
                    earlyLogger?.Info("[BOOT] Service mode: host.RunAsync() returned a Task.");
                    earlyLogger?.Info("[BOOT] host.RunAsync() returned Task; awaiting completion...");
                    runTask.GetAwaiter().GetResult();
                    earlyLogger?.Info("[BOOT] Service mode: host.RunAsync completed normally.");
                    earlyLogger?.Info("[BOOT] host.RunAsync() completed normally. Host stopped.");
                    return 0;
                }
                catch (Exception ex)
                {
                    // Use the same logging style / overloads as the existing global catch.
                    earlyLogger?.Error("[BOOT] Service mode: host.RunAsync() threw a fatal exception.", ex);
                    earlyLogger?.Error("[BOOT] host.RunAsync() threw a fatal exception.", ex);
                    throw;
                }
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
            return 1;
        }
    }
}

