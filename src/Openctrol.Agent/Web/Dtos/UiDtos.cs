using System.Text.Json.Serialization;

namespace Openctrol.Agent.Web.Dtos;

public sealed class UiStatusDto
{
    [JsonPropertyName("agent")]
    public AgentInfoDto Agent { get; init; } = new();
    
    [JsonPropertyName("service")]
    public ServiceInfoDto Service { get; init; } = new();
    
    [JsonPropertyName("health")]
    public HealthInfoDto Health { get; init; } = new();
    
    [JsonPropertyName("config")]
    public ConfigSummaryDto Config { get; init; } = new();
}

public sealed class AgentInfoDto
{
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = "";
    
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";
    
    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; init; }
}

public sealed class ServiceInfoDto
{
    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "OpenctrolAgent";
    
    [JsonPropertyName("is_service_installed")]
    public bool IsServiceInstalled { get; init; }
    
    [JsonPropertyName("service_status")]
    public string ServiceStatus { get; init; } = "Unknown"; // Running, Stopped, Unknown
}

public sealed class HealthInfoDto
{
    [JsonPropertyName("desktop_state")]
    public string DesktopState { get; init; } = "unknown"; // desktop, locked, login_screen, unknown
    
    [JsonPropertyName("is_degraded")]
    public bool IsDegraded { get; init; }
    
    [JsonPropertyName("active_sessions")]
    public int ActiveSessions { get; init; }
}

public sealed class ConfigSummaryDto
{
    [JsonPropertyName("port")]
    public int Port { get; init; }
    
    [JsonPropertyName("use_https")]
    public bool UseHttps { get; init; }
    
    [JsonPropertyName("api_key_configured")]
    public bool ApiKeyConfigured { get; init; }
    
    [JsonPropertyName("allowed_ha_ids")]
    public IList<string> AllowedHaIds { get; init; } = new List<string>();
}

public sealed class UiConfigDto
{
    [JsonPropertyName("port")]
    public int Port { get; init; }
    
    [JsonPropertyName("use_https")]
    public bool UseHttps { get; init; }
    
    [JsonPropertyName("cert_path")]
    public string CertPath { get; init; } = "";
    
    [JsonPropertyName("api_key_configured")]
    public bool ApiKeyConfigured { get; init; }
    
    [JsonPropertyName("allowed_ha_ids")]
    public IList<string> AllowedHaIds { get; init; } = new List<string>();
    
    [JsonPropertyName("allow_empty_api_key")]
    public bool AllowEmptyApiKey { get; init; }
    
    [JsonPropertyName("require_auth_for_health")]
    public bool RequireAuthForHealth { get; init; }
}

public sealed class UiConfigUpdateRequest
{
    [JsonPropertyName("port")]
    public int? Port { get; init; }
    
    [JsonPropertyName("use_https")]
    public bool? UseHttps { get; init; }
    
    [JsonPropertyName("cert_path")]
    public string? CertPath { get; init; }
    
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }
    
    [JsonPropertyName("allowed_ha_ids")]
    public IList<string>? AllowedHaIds { get; init; }
    
    [JsonPropertyName("allow_empty_api_key")]
    public bool? AllowEmptyApiKey { get; init; }
    
    [JsonPropertyName("require_auth_for_health")]
    public bool? RequireAuthForHealth { get; init; }
}

public sealed class ServiceControlResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
    
    [JsonPropertyName("service_status")]
    public string ServiceStatus { get; init; } = "Unknown";
    
    [JsonPropertyName("error")]
    public string? Error { get; init; }
    
    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

