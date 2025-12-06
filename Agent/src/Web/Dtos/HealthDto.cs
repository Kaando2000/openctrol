using System.Text.Json.Serialization;

namespace Openctrol.Agent.Web.Dtos;

public sealed class HealthDto
{
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = "";
    
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";
    
    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; init; }
    
    [JsonPropertyName("remote_desktop")]
    public RemoteDesktopHealthDto RemoteDesktop { get; init; } = new();
    
    [JsonPropertyName("active_sessions")]
    public int ActiveSessions { get; init; }
}

public sealed class RemoteDesktopHealthDto
{
    [JsonPropertyName("is_running")]
    public bool IsRunning { get; init; }
    
    [JsonPropertyName("last_frame_at")]
    public DateTimeOffset LastFrameAt { get; init; }
    
    [JsonPropertyName("state")]
    public string State { get; init; } = "unknown";
    
    [JsonPropertyName("desktop_state")]
    public string DesktopState { get; init; } = "unknown";
    
    [JsonPropertyName("degraded")]
    public bool Degraded { get; init; } = false;
}

