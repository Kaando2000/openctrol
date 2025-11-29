namespace Openctrol.Agent.Config;

public sealed class AgentConfig
{
    public string AgentId { get; set; } = "";
    public int HttpPort { get; set; } = 44325;
    public int MaxSessions { get; set; } = 1;
    public string CertPath { get; set; } = "";
    public string CertPasswordEncrypted { get; set; } = "";
    public int TargetFps { get; set; } = 30;
    public IList<string> AllowedHaIds { get; set; } = new List<string>();
}

