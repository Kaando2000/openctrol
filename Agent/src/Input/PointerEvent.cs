namespace Openctrol.Agent.Input;

public sealed class PointerEvent
{
    public PointerEventKind Kind { get; init; }
    public int Dx { get; init; }
    public int Dy { get; init; }
    public int? AbsoluteX { get; init; }
    public int? AbsoluteY { get; init; }
    public MouseButton? Button { get; init; }
    public MouseButtonAction? ButtonAction { get; init; }
    public int WheelDeltaX { get; init; }
    public int WheelDeltaY { get; init; }
}

