namespace Openctrol.Agent.Security;

public sealed class SessionToken
{
    public string Token { get; init; } = "";
    public string HaId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

