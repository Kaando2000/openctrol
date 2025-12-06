namespace Openctrol.Agent.Input;

public sealed class KeyboardEvent
{
    public KeyboardEventKind Kind { get; init; }
    public int? KeyCode { get; init; }
    public string? Text { get; init; }
    public KeyModifiers Modifiers { get; init; }
}

