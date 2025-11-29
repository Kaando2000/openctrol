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
using Openctrol.Agent.Discovery;

namespace Openctrol.Agent;

public static class Program
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices((ctx, services) =>
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
                services.AddSingleton<IDiscoveryBroadcaster, MdnsDiscoveryBroadcaster>();
                
                services.AddSingleton<IControlApiServer, ControlApiServer>();

                // Register AgentHost as both hosted service and uptime service
                services.AddSingleton<AgentHost>();
                services.AddSingleton<IUptimeService>(sp => sp.GetRequiredService<AgentHost>());
                services.AddHostedService<AgentHost>(sp => sp.GetRequiredService<AgentHost>());
            })
            .Build()
            .Run();
    }
}

