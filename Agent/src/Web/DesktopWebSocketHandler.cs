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
    private readonly SemaphoreSlim _sendLock = new(1, 1); // Ensure only one frame send at a time
    
    // Input rate limiting: max 1000 events per second per session
    private const int MaxInputEventsPerSecond = 1000;
    private const int MaxRateLimitQueueSize = 2000; // 2 seconds worth at max rate (1000/sec * 2)
    private readonly Queue<DateTime> _inputEventTimestamps = new();
    private readonly object _rateLimitLock = new();

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
            // Register this handler with session broker for immediate termination support.
            // When SessionBroker.EndSession() is called, it will cancel this CancellationTokenSource,
            // which will cause all three tasks (receive, send, expiry check) to detect cancellation
            // and exit cleanly, closing the WebSocket connection.
            _sessionBroker.RegisterWebSocketHandler(_sessionId, _cancellationTokenSource);
            
            // Send hello message
            await SendHelloAsync();

            // Subscribe to frames
            _remoteDesktopEngine.RegisterFrameSubscriber(this);
            _isSubscribed = true;
            _logger.Info($"[WebSocket] Client connected for session {_sessionId}");

            // Start frame sending task
            var sendTask = SendFramesAsync(_cancellationTokenSource.Token);
            
            // Start receiving messages (with cancellation support)
            var receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
            
            // Start session expiry check task
            var expiryCheckTask = CheckSessionExpiryAsync(_cancellationTokenSource.Token);
            
            // Wait for any task to complete (receive, send, or expiry check)
            await Task.WhenAny(receiveTask, sendTask, expiryCheckTask);
            
            // Cancel to signal all tasks to stop
            _cancellationTokenSource.Cancel();
            
            // Wait for all tasks to complete to ensure clean shutdown
            // This prevents race conditions where one task might still be accessing
            // the WebSocket or channel while cleanup is happening
            try
            {
                await Task.WhenAll(receiveTask, sendTask, expiryCheckTask);
            }
            catch (Exception ex)
            {
                // Log but don't throw - we're shutting down anyway
                _logger.Error($"[WebSocket] Error waiting for tasks to complete in session {_sessionId}", ex);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[WebSocket] Error in handler for session {_sessionId}", ex);
        }
        finally
        {
            // Unregister handler from session broker
            _sessionBroker.UnregisterWebSocketHandler(_sessionId);
            
            // Ensure cancellation is set (in case exception occurred before cancellation)
            _cancellationTokenSource.Cancel();
            
            if (_isSubscribed)
            {
                _remoteDesktopEngine.UnregisterFrameSubscriber(this);
                _isSubscribed = false;
            }

            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[WebSocket] Error closing WebSocket for session {_sessionId}: {ex.Message}");
                }
            }

            // Note: Session cleanup is handled by:
            // 1. REST API EndSession call -> signals cancellation and removes session
            // 2. Session expiry check -> removes expired sessions periodically
            // We don't call EndSession here to avoid double-cleanup
            _cancellationTokenSource.Dispose();
            _logger.Info($"[WebSocket] Client disconnected for session {_sessionId}");
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

                // Use semaphore to ensure only one send at a time (backpressure strategy)
                // Wait briefly (5ms) before dropping frame to allow ongoing send to complete
                // This reduces unnecessary frame drops while still maintaining backpressure
                if (!await _sendLock.WaitAsync(TimeSpan.FromMilliseconds(5), cancellationToken))
                {
                    // Send is taking too long or cancelled, drop this frame to prevent queue buildup
                    continue;
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
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (System.Net.WebSockets.WebSocketException ex)
                {
                    _logger.Error($"[WebSocket] WebSocket error sending frame in session {_sessionId}: {ex.Message}", ex);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"[WebSocket] Error sending frame in session {_sessionId}: {ex.Message}", ex);
                    break;
                }
                finally
                {
                    _sendLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.Error($"[WebSocket] Error in frame sending loop for session {_sessionId}", ex);
        }
        finally
        {
            _frameQueue.Writer.Complete();
            _sendLock.Dispose();
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

    /// <summary>
    /// Maximum allowed WebSocket message size: 64KB.
    /// Messages exceeding this limit will cause the connection to be closed immediately.
    /// This prevents DoS attacks via oversized messages and limits memory usage.
    /// </summary>
    private const int MaxWebSocketMessageSize = 64 * 1024; // 64KB

    /// <summary>
    /// Receives and processes WebSocket messages with explicit size limit enforcement.
    /// 
    /// Message size enforcement:
    /// - Messages are accumulated across multiple ReceiveAsync calls until EndOfMessage is true.
    /// - The cumulative size is tracked and checked after each chunk.
    /// - If totalSize exceeds MaxWebSocketMessageSize (64KB), the connection is immediately closed
    ///   with WebSocketCloseStatus.MessageTooBig and a clear reason.
    /// - A warning is logged with the session ID and actual message size.
    /// </summary>
    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        // Use ArrayPool for efficient buffer management
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // Accumulate message chunks until EndOfMessage
                // This handles messages that span multiple ReceiveAsync calls
                // Pre-allocate with estimated capacity to reduce reallocations
                var messageBuffer = new List<byte>(4096);
                var totalSize = 0; // Track cumulative message size for limit enforcement
                WebSocketReceiveResult result;

                do
                {
                    try
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        return;
                    }
                    catch (System.Net.WebSockets.WebSocketException ex)
                    {
                        _logger.Error($"[WebSocket] WebSocket error in receive loop for session {_sessionId}: {ex.Message}", ex);
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.Debug($"[WebSocket] Received close message for session {_sessionId}");
                        return;
                    }

                    // MESSAGE SIZE LIMIT ENFORCEMENT:
                    // Check cumulative size after each chunk. If limit exceeded, close connection immediately.
                    totalSize += result.Count;
                    if (totalSize > MaxWebSocketMessageSize)
                    {
                        _logger.Warn($"[WebSocket] Message size limit exceeded: received {totalSize} bytes (limit: {MaxWebSocketMessageSize} bytes) for session {_sessionId}. Closing connection.");
                        try
                        {
                            await _webSocket.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                $"Message size {totalSize} bytes exceeds limit of {MaxWebSocketMessageSize} bytes",
                                CancellationToken.None);
                        }
                        catch
                        {
                            // Ignore errors when closing - connection is being terminated anyway
                        }
                        return; // Exit receive loop - connection is closed
                    }

                    // Accumulate message chunks using efficient array copy instead of AddRange with Take
                    // This avoids creating enumerators and is more performant
                    // Copy chunk to temporary array, then add to list
                    var chunk = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, result.Count);
                    messageBuffer.AddRange(chunk);

                } while (!result.EndOfMessage); // Continue until complete message received

                // Process complete message (only if within size limit)
                if (result.MessageType == WebSocketMessageType.Text && messageBuffer.Count > 0)
                {
                    var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    HandleMessage(message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[WebSocket] Error in receive loop for session {_sessionId}", ex);
        }
        finally
        {
            // Return buffer to pool
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task CheckSessionExpiryAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check session expiry every 10 seconds
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                // Check if session has expired
                if (!_sessionBroker.TryGetSession(_sessionId, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    _logger.Info($"[WebSocket] Session {_sessionId} expired, closing connection");
                    
                    // Send close message if possible
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await _webSocket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Session expired",
                                CancellationToken.None);
                        }
                        catch
                        {
                            // Ignore errors when closing
                        }
                    }
                    
                    // Cancel to trigger shutdown
                    _cancellationTokenSource.Cancel();
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
            _logger.Error($"[WebSocket] Error in session expiry check for session {_sessionId}", ex);
        }
    }

    private void HandleMessage(string messageJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(messageJson))
            {
                _logger.Debug($"[WebSocket] Received empty message in session {_sessionId}");
                return;
            }

            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                _logger.Debug($"[WebSocket] Message missing 'type' field in session {_sessionId}");
                return;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrEmpty(type))
            {
                _logger.Debug($"[WebSocket] Message has empty 'type' field in session {_sessionId}");
                return;
            }
            
            // Check rate limiting for input messages (pointer/keyboard events)
            bool isInputMessage = type == "pointer_move" || type == "pointer_button" || 
                                 type == "pointer_wheel" || type == "key" || type == "text";
            
            if (isInputMessage && !CheckInputRateLimit())
            {
                _logger.Warn($"[WebSocket] Input rate limit exceeded for session {_sessionId}. Dropping message.");
                return; // Drop the message silently to prevent DoS
            }
            
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
                    // Quality setting - not implemented in v1, silently ignore
                    break;
                default:
                    _logger.Debug($"[WebSocket] Unknown message type '{type}' in session {_sessionId}");
                    // Log but don't close connection - just ignore unknown types
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.Warn($"[WebSocket] Invalid JSON in message from session {_sessionId}: {ex.Message}");
            // Don't close connection for malformed JSON - just log and ignore
        }
        catch (Exception ex)
        {
            _logger.Error($"[WebSocket] Error handling message in session {_sessionId}", ex);
            // Don't close connection - log error but continue processing
        }
    }

    /// <summary>
    /// Checks if input event is within rate limit (sliding window: max N events per second).
    /// Returns true if event should be processed, false if rate limit exceeded.
    /// Includes emergency cleanup to prevent unbounded queue growth.
    /// </summary>
    private bool CheckInputRateLimit()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-1);

            // Remove timestamps outside the 1-second window
            while (_inputEventTimestamps.Count > 0 && _inputEventTimestamps.Peek() < windowStart)
            {
                _inputEventTimestamps.Dequeue();
            }

            // Emergency cleanup if queue gets too large (prevents memory exhaustion under attack)
            if (_inputEventTimestamps.Count > MaxRateLimitQueueSize)
            {
                // Remove oldest entries to bring it down to half the max size
                while (_inputEventTimestamps.Count > MaxRateLimitQueueSize / 2)
                {
                    _inputEventTimestamps.Dequeue();
                }
                _logger.Warn($"[WebSocket] Rate limit queue exceeded max size, emergency cleanup performed for session {_sessionId}");
            }

            // Check if we're at the limit
            if (_inputEventTimestamps.Count >= MaxInputEventsPerSecond)
            {
                return false; // Rate limit exceeded
            }

            // Record this event
            _inputEventTimestamps.Enqueue(now);
            return true; // Within rate limit
        }
    }

    private void HandlePointerMove(JsonElement root)
    {
        try
        {
            var dx = root.TryGetProperty("dx", out var dxEl) ? dxEl.GetInt32() : 0;
            var dy = root.TryGetProperty("dy", out var dyEl) ? dyEl.GetInt32() : 0;
            var absolute = root.TryGetProperty("absolute", out var absEl) && absEl.GetBoolean();

            PointerEvent evt;
            if (absolute && root.TryGetProperty("x", out var xEl) && root.TryGetProperty("y", out var yEl))
            {
                // Validate absolute coordinates (normalized 0-65535 range for Windows SendInput)
                var x = xEl.GetInt32();
                var y = yEl.GetInt32();
                
                // Clamp to valid normalized range
                x = Math.Clamp(x, 0, 65535);
                y = Math.Clamp(y, 0, 65535);
                
                evt = new PointerEvent
                {
                    Kind = PointerEventKind.MoveAbsolute,
                    AbsoluteX = x,
                    AbsoluteY = y
                };
            }
            else
            {
                // Validate relative move coordinates (Windows SendInput limit is ±32767)
                dx = Math.Clamp(dx, -32767, 32767);
                dy = Math.Clamp(dy, -32767, 32767);
                
                evt = new PointerEvent
                {
                    Kind = PointerEventKind.MoveRelative,
                    Dx = dx,
                    Dy = dy
                };
            }

            _remoteDesktopEngine.InjectPointer(evt);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling pointer move in session {_sessionId}: {ex.Message}", ex);
        }
    }

    private void HandlePointerButton(JsonElement root)
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling pointer button in session {_sessionId}: {ex.Message}", ex);
        }
    }

    private void HandlePointerWheel(JsonElement root)
    {
        try
        {
            var deltaX = root.TryGetProperty("delta_x", out var dxEl) ? dxEl.GetInt32() : 0;
            var deltaY = root.TryGetProperty("delta_y", out var dyEl) ? dyEl.GetInt32() : 0;

            // Validate wheel delta values (Windows SendInput expects range -32768 to 32767)
            deltaX = Math.Clamp(deltaX, -32768, 32767);
            deltaY = Math.Clamp(deltaY, -32768, 32767);

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
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling pointer wheel in session {_sessionId}: {ex.Message}", ex);
        }
    }

    private void HandleKey(JsonElement root)
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling key event in session {_sessionId}: {ex.Message}", ex);
        }
    }

    private void HandleText(JsonElement root)
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling text event in session {_sessionId}: {ex.Message}", ex);
        }
    }

    private void HandleMonitorSelect(JsonElement root)
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error($"[Input] Error handling monitor select in session {_sessionId}: {ex.Message}", ex);
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

