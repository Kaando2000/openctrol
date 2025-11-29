using System.Threading.Channels;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Openctrol.Agent.Config;
using Openctrol.Agent.Input;
using Openctrol.Agent.RemoteDesktop;
using Openctrol.Agent.Security;
using Openctrol.Agent.Web.Dtos;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Web;

public sealed class DesktopWebSocketHandler : IFrameSubscriber
{
    private readonly WebSocket _webSocket;
    private readonly IConfigManager _configManager;
    private readonly ISecurityManager _securityManager;
    private readonly ISessionBroker _sessionBroker;
    private readonly IRemoteDesktopEngine _remoteDesktopEngine;
    private readonly ILogger _logger;
    private readonly string _sessionId;
    private readonly string _agentId;
    private bool _isSubscribed;
    private readonly Channel<RemoteFrame> _frameQueue;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public DesktopWebSocketHandler(
        WebSocket webSocket,
        IConfigManager configManager,
        ISecurityManager securityManager,
        ISessionBroker sessionBroker,
        IRemoteDesktopEngine remoteDesktopEngine,
        ILogger logger,
        string sessionId,
        string agentId)
    {
        _webSocket = webSocket;
        _configManager = configManager;
        _securityManager = securityManager;
        _sessionBroker = sessionBroker;
        _remoteDesktopEngine = remoteDesktopEngine;
        _logger = logger;
        _sessionId = sessionId;
        _agentId = agentId;
        
        // Create bounded channel for frame queue (drop frames if queue is full to prevent memory issues)
        var options = new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _frameQueue = Channel.CreateBounded<RemoteFrame>(options);
    }

    public async Task HandleAsync()
    {
        try
        {
            // Send hello message
            await SendHelloAsync();

            // Subscribe to frames
            _remoteDesktopEngine.RegisterFrameSubscriber(this);
            _isSubscribed = true;
            _logger.Info($"WebSocket client connected for session {_sessionId}");

            // Start frame sending task
            var sendTask = SendFramesAsync(_cancellationTokenSource.Token);
            
            // Start receiving messages
            var receiveTask = ReceiveMessagesAsync();
            
            // Wait for either task to complete
            await Task.WhenAny(receiveTask, sendTask);
            
            // Cancel frame sending
            _cancellationTokenSource.Cancel();
        }
        catch (Exception ex)
        {
            _logger.Error($"Error in WebSocket handler for session {_sessionId}", ex);
        }
        finally
        {
            if (_isSubscribed)
            {
                _remoteDesktopEngine.UnregisterFrameSubscriber(this);
                _isSubscribed = false;
            }

            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
            }

            _sessionBroker.EndSession(_sessionId);
            _cancellationTokenSource.Dispose();
            _logger.Info($"WebSocket client disconnected for session {_sessionId}");
        }
    }

    public void OnFrame(RemoteFrame frame)
    {
        // Queue frame for async sending - non-blocking
        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                _frameQueue.Writer.TryWrite(frame);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error queueing frame", ex);
        }
    }

    private async Task SendFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _frameQueue.Reader.ReadAllAsync(cancellationToken))
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    break;
                }

                try
                {
                    // Send frame in binary format: OFRA header + JPEG
                    var header = new byte[16];
                    Encoding.ASCII.GetBytes("OFRA").CopyTo(header, 0);
                    BitConverter.GetBytes(frame.Width).CopyTo(header, 4);
                    BitConverter.GetBytes(frame.Height).CopyTo(header, 8);
                    BitConverter.GetBytes((int)frame.Format).CopyTo(header, 12);

                    var frameData = new byte[header.Length + frame.Payload.Length];
                    header.CopyTo(frameData, 0);
                    frame.Payload.Span.CopyTo(frameData.AsSpan(header.Length));

                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(frameData),
                        WebSocketMessageType.Binary,
                        true,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error("Error sending frame to WebSocket client", ex);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.Error("Error in frame sending loop", ex);
        }
        finally
        {
            _frameQueue.Writer.Complete();
        }
    }

    private async Task SendHelloAsync()
    {
        var config = _configManager.GetConfig();
        var monitors = _remoteDesktopEngine.GetMonitors();

        var hello = new
        {
            type = "hello",
            agent_id = _agentId,
            session_id = _sessionId,
            version = "1.0",
            monitors = monitors.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                width = m.Width,
                height = m.Height,
                is_primary = m.IsPrimary
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(hello);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[4096];
        while (_webSocket.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessageAsync(message);
            }
        }
    }

    private async Task HandleMessageAsync(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            
            switch (type)
            {
                case "pointer_move":
                    HandlePointerMove(root);
                    break;
                case "pointer_button":
                    HandlePointerButton(root);
                    break;
                case "pointer_wheel":
                    HandlePointerWheel(root);
                    break;
                case "key":
                    HandleKey(root);
                    break;
                case "text":
                    HandleText(root);
                    break;
                case "monitor_select":
                    HandleMonitorSelect(root);
                    break;
                case "quality":
                    // Quality setting - not implemented yet
                    break;
                default:
                    _logger.Warn($"Unknown message type: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error handling WebSocket message", ex);
        }
    }

    private void HandlePointerMove(JsonElement root)
    {
        var dx = root.TryGetProperty("dx", out var dxEl) ? dxEl.GetInt32() : 0;
        var dy = root.TryGetProperty("dy", out var dyEl) ? dyEl.GetInt32() : 0;
        var absolute = root.TryGetProperty("absolute", out var absEl) && absEl.GetBoolean();

        PointerEvent evt;
        if (absolute && root.TryGetProperty("x", out var xEl) && root.TryGetProperty("y", out var yEl))
        {
            evt = new PointerEvent
            {
                Kind = PointerEventKind.MoveAbsolute,
                AbsoluteX = xEl.GetInt32(),
                AbsoluteY = yEl.GetInt32()
            };
        }
        else
        {
            evt = new PointerEvent
            {
                Kind = PointerEventKind.MoveRelative,
                Dx = dx,
                Dy = dy
            };
        }

        _remoteDesktopEngine.InjectPointer(evt);
    }

    private void HandlePointerButton(JsonElement root)
    {
        if (!root.TryGetProperty("button", out var buttonEl) || !root.TryGetProperty("action", out var actionEl))
        {
            return;
        }

        var buttonStr = buttonEl.GetString();
        var actionStr = actionEl.GetString();

        MouseButton? button = buttonStr?.ToLower() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => null
        };

        MouseButtonAction? action = actionStr?.ToLower() switch
        {
            "down" => MouseButtonAction.Down,
            "up" => MouseButtonAction.Up,
            _ => null
        };

        if (button.HasValue && action.HasValue)
        {
            var evt = new PointerEvent
            {
                Kind = PointerEventKind.Button,
                Button = button.Value,
                ButtonAction = action.Value
            };

            _remoteDesktopEngine.InjectPointer(evt);
        }
    }

    private void HandlePointerWheel(JsonElement root)
    {
        var deltaX = root.TryGetProperty("delta_x", out var dxEl) ? dxEl.GetInt32() : 0;
        var deltaY = root.TryGetProperty("delta_y", out var dyEl) ? dyEl.GetInt32() : 0;

        if (deltaX != 0 || deltaY != 0)
        {
            var evt = new PointerEvent
            {
                Kind = PointerEventKind.Wheel,
                WheelDeltaX = deltaX,
                WheelDeltaY = deltaY
            };

            _remoteDesktopEngine.InjectPointer(evt);
        }
    }

    private void HandleKey(JsonElement root)
    {
        if (!root.TryGetProperty("key_code", out var keyCodeEl))
        {
            return;
        }

        var keyCode = keyCodeEl.GetInt32();
        var action = root.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : "down";
        var modifiers = ParseModifiers(root);

        var evt = new KeyboardEvent
        {
            Kind = action == "up" ? KeyboardEventKind.KeyUp : KeyboardEventKind.KeyDown,
            KeyCode = keyCode,
            Modifiers = modifiers
        };

        _remoteDesktopEngine.InjectKey(evt);
    }

    private void HandleText(JsonElement root)
    {
        if (!root.TryGetProperty("text", out var textEl))
        {
            return;
        }

        var text = textEl.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var modifiers = ParseModifiers(root);
        var evt = new KeyboardEvent
        {
            Kind = KeyboardEventKind.Text,
            Text = text,
            Modifiers = modifiers
        };

        _remoteDesktopEngine.InjectKey(evt);
    }

    private void HandleMonitorSelect(JsonElement root)
    {
        if (root.TryGetProperty("monitor_id", out var monitorIdEl))
        {
            var monitorId = monitorIdEl.GetString();
            if (!string.IsNullOrEmpty(monitorId))
            {
                _remoteDesktopEngine.SelectMonitor(monitorId);
            }
        }
    }

    private KeyModifiers ParseModifiers(JsonElement root)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        if (root.TryGetProperty("ctrl", out var ctrlEl) && ctrlEl.GetBoolean())
        {
            modifiers |= KeyModifiers.Ctrl;
        }
        if (root.TryGetProperty("alt", out var altEl) && altEl.GetBoolean())
        {
            modifiers |= KeyModifiers.Alt;
        }
        if (root.TryGetProperty("shift", out var shiftEl) && shiftEl.GetBoolean())
        {
            modifiers |= KeyModifiers.Shift;
        }
        if (root.TryGetProperty("win", out var winEl) && winEl.GetBoolean())
        {
            modifiers |= KeyModifiers.Win;
        }
        return modifiers;
    }
}

