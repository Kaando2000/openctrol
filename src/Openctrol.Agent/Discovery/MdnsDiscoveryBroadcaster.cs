using System.Reflection;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Discovery;

public sealed class MdnsDiscoveryBroadcaster : IDiscoveryBroadcaster
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private bool _isRunning;

    public MdnsDiscoveryBroadcaster(IConfigManager configManager, ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        try
        {
            var config = _configManager.GetConfig();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            // TODO: Implement full mDNS advertising using Makaretu.MDNS or similar
            // For now, this is a stub that logs the service info
            _logger.Info($"mDNS service would be advertised: _openctrol._tcp.local on port {config.HttpPort}");
            _logger.Info($"TXT records: id={config.AgentId}, ver={version}, cap=desktop,input");

            _isRunning = true;
        }
        catch (Exception ex)
        {
            _logger.Error("Error starting mDNS discovery broadcaster", ex);
        }
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _logger.Info("mDNS discovery broadcaster stopped");
    }
}

