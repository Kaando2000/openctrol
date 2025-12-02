namespace Openctrol.Agent.Config;

public sealed class AgentConfig
{
    public string AgentId { get; set; } = "";
    public int HttpPort { get; set; } = 44325;
    public int MaxSessions { get; set; } = 1;
    public string CertPath { get; set; } = "";
    public string CertPasswordEncrypted { get; set; } = "";
    public int TargetFps { get; set; } = 30;
    
    // NOTE: AllowedHaIds is deprecated and no longer used. Authentication is based on API key + LAN/localhost only.
    // This property is kept for backward compatibility with existing config.json files.
    public IList<string> AllowedHaIds { get; set; } = new List<string>();
    
    public string ApiKey { get; set; } = ""; // API key for REST endpoint authentication (empty = no auth required)
}

