namespace Openctrol.Agent.RemoteDesktop;

public sealed class RemoteFrame
{
    public int Width { get; init; }
    public int Height { get; init; }
    public FramePixelFormat Format { get; init; }
    public ReadOnlyMemory<byte> Payload { get; init; }
    public long SequenceNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

