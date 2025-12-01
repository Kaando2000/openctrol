namespace Openctrol.Agent.Web.Dtos;

public sealed class PointerInputMessage
{
    public string Type { get; init; } = "pointer";
    public string Event { get; init; } = ""; // "move", "click", "scroll"
    public float Dx { get; init; }
    public float Dy { get; init; }
    public string? Button { get; init; } = null; // "left", "right", "middle" - for click events
}

public sealed class KeyboardInputMessage
{
    public string Type { get; init; } = "keyboard";
    public IReadOnlyList<string> Keys { get; init; } = Array.Empty<string>();
}

public sealed class ErrorMessage
{
    public string Type { get; init; } = "error";
    public string Message { get; init; } = "";
}

