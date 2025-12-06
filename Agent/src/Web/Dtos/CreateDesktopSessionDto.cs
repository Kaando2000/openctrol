namespace Openctrol.Agent.Web.Dtos;

public sealed class CreateDesktopSessionRequest
{
    public string HaId { get; init; } = "";
    public int TtlSeconds { get; init; } = 900;
}

public sealed class CreateDesktopSessionResponse
{
    public string SessionId { get; init; } = "";
    public string WebSocketUrl { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

