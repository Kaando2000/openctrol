namespace Openctrol.Agent.Web.Dtos;

public sealed class PowerRequest
{
    public string Action { get; init; } = ""; // "restart" | "shutdown" | "wol"
    public bool Force { get; init; } = false; // Optional, reserved for future use
}

public sealed class PowerResponse
{
    public string Status { get; init; } = "ok";
    public string Action { get; init; } = "";
}

