namespace Openctrol.Agent.Web.Dtos;

public sealed class PowerRequest
{
    public string Action { get; init; } = ""; // "restart" | "shutdown"
}

