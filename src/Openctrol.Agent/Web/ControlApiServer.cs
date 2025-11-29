using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Openctrol.Agent.Audio;
using Openctrol.Agent.Config;
using Openctrol.Agent.Hosting;
using Openctrol.Agent.Power;
using Openctrol.Agent.RemoteDesktop;
using Openctrol.Agent.Security;
using Openctrol.Agent.Web.Dtos;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Web;

public sealed class ControlApiServer : IControlApiServer
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly ISecurityManager? _securityManager;
    private readonly ISessionBroker? _sessionBroker;
    private readonly IRemoteDesktopEngine? _remoteDesktopEngine;
    private readonly IPowerManager? _powerManager;
    private readonly IAudioManager? _audioManager;
    private readonly IUptimeService? _uptimeService;
    private WebApplication? _app;
    private Task? _runTask;
    private X509Certificate2? _certificate; // Store to prevent disposal before Kestrel uses it

    public ControlApiServer(
        IConfigManager configManager,
        ILogger logger,
        ISecurityManager? securityManager = null,
        ISessionBroker? sessionBroker = null,
        IRemoteDesktopEngine? remoteDesktopEngine = null,
        IPowerManager? powerManager = null,
        IAudioManager? audioManager = null,
        IUptimeService? uptimeService = null)
    {
        _configManager = configManager;
        _logger = logger;
        _securityManager = securityManager;
        _sessionBroker = sessionBroker;
        _remoteDesktopEngine = remoteDesktopEngine;
        _powerManager = powerManager;
        _audioManager = audioManager;
        _uptimeService = uptimeService;
    }

    public void Start()
    {
        var config = _configManager.GetConfig();
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Configure HTTPS if certificate is available
            if (!string.IsNullOrEmpty(config.CertPath) && File.Exists(config.CertPath))
            {
                try
                {
                    var certPassword = DecryptCertPassword(config.CertPasswordEncrypted);
                    _certificate = new X509Certificate2(config.CertPath, certPassword);
                    options.ListenAnyIP(config.HttpPort, listenOptions =>
                    {
                        listenOptions.UseHttps(_certificate);
                    });
                    _logger.Info($"HTTPS enabled on port {config.HttpPort} with certificate from {config.CertPath}");
                }
                catch (Exception ex)
                {
                    _logger.Error("Error loading certificate, falling back to HTTP", ex);
                    _certificate?.Dispose();
                    _certificate = null;
                    options.ListenAnyIP(config.HttpPort);
                }
            }
            else
            {
                // Fallback to HTTP if no certificate configured
                options.ListenAnyIP(config.HttpPort);
                _logger.Warn($"HTTP mode (no certificate configured). Listening on port {config.HttpPort}");
            }
        });

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        _app = builder.Build();

        // Health endpoint
        _app.MapGet("/api/v1/health", () =>
        {
            var config = _configManager.GetConfig();
            var activeSessions = _sessionBroker?.GetActiveSessions().Count ?? 0;
            var remoteDesktopStatus = _remoteDesktopEngine?.GetStatus();

            var remoteDesktop = remoteDesktopStatus != null
                ? new RemoteDesktopHealthDto
                {
                    IsRunning = remoteDesktopStatus.IsRunning,
                    LastFrameAt = remoteDesktopStatus.LastFrameAt,
                    State = remoteDesktopStatus.State
                }
                : new RemoteDesktopHealthDto
                {
                    IsRunning = false,
                    LastFrameAt = DateTimeOffset.MinValue,
                    State = "unknown"
                };

            var uptimeSeconds = _uptimeService?.GetUptimeSeconds() ?? 0;
            var health = new HealthDto
            {
                AgentId = config.AgentId,
                UptimeSeconds = uptimeSeconds,
                RemoteDesktop = remoteDesktop,
                ActiveSessions = activeSessions
            };

            return Results.Json(health);
        });

        // Create desktop session
        _app.MapPost("/api/v1/sessions/desktop", async (HttpContext context) =>
        {
            if (_sessionBroker == null || _securityManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Session broker or security manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<CreateDesktopSessionRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.HaId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "HA ID is required" }, statusCode: 400);
                }

                // Validate HA ID is allowed
                if (!_securityManager.IsHaAllowed(request.HaId))
                {
                    _logger.Warn($"Rejected session creation request from unauthorized HA ID: {request.HaId}");
                    return Results.Json(new ErrorResponse { Error = "unauthorized", Details = "HA ID not allowed" }, statusCode: 401);
                }

                var ttl = TimeSpan.FromSeconds(Math.Max(60, Math.Min(3600, request.TtlSeconds))); // Clamp between 60s and 1h
                var token = _securityManager.IssueDesktopSessionToken(request.HaId, ttl);
                var session = _sessionBroker.StartDesktopSession(request.HaId, ttl);

                _logger.Info($"Desktop session created: {session.SessionId} for HA ID: {request.HaId}");

                var wsScheme = context.Request.Scheme == "https" ? "wss" : "ws";
                var wsUrl = $"{wsScheme}://{context.Request.Host}/ws/desktop?sess={session.SessionId}&token={token.Token}";

                var response = new CreateDesktopSessionResponse
                {
                    SessionId = session.SessionId,
                    WebSocketUrl = wsUrl,
                    ExpiresAt = session.ExpiresAt
                };

                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.Error("Error creating desktop session", ex);
                return Results.Json(new ErrorResponse { Error = "session_creation_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // End desktop session
        _app.MapPost("/api/v1/sessions/desktop/{id}/end", (string id) =>
        {
            if (_sessionBroker == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Session broker not available" }, statusCode: 503);
            }

            if (string.IsNullOrEmpty(id))
            {
                return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Session ID is required" }, statusCode: 400);
            }

            try
            {
                _sessionBroker.EndSession(id);
                _logger.Info($"Desktop session ended: {id}");
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error ending session {id}", ex);
                return Results.Json(new ErrorResponse { Error = "session_end_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // Power control
        _app.MapPost("/api/v1/power", async (HttpContext context) =>
        {
            if (_powerManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Power manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<PowerRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Action is required (restart or shutdown)" }, statusCode: 400);
                }

                var action = request.Action.ToLower();
                switch (action)
                {
                    case "restart":
                        _logger.Warn($"System restart requested via API");
                        _powerManager.Restart();
                        return Results.Ok();
                    case "shutdown":
                        _logger.Warn($"System shutdown requested via API");
                        _powerManager.Shutdown();
                        return Results.Ok();
                    default:
                        return Results.Json(new ErrorResponse { Error = "invalid_request", Details = $"Unknown action: {request.Action}. Must be 'restart' or 'shutdown'" }, statusCode: 400);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error handling power request", ex);
                return Results.Json(new ErrorResponse { Error = "power_operation_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // Audio state
        _app.MapGet("/api/v1/audio/state", () =>
        {
            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                var state = _audioManager.GetState();
                var response = new AudioStateResponse
                {
                    DefaultOutputDeviceId = state.DefaultOutputDeviceId,
                    Devices = state.Devices.Select(d => new AudioDeviceInfoDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Volume = d.Volume,
                        Muted = d.Muted,
                        IsDefault = d.IsDefault
                    }).ToList(),
                    Sessions = state.Sessions.Select(s => new AudioSessionInfoDto
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Volume = s.Volume,
                        Muted = s.Muted,
                        OutputDeviceId = s.OutputDeviceId
                    }).ToList()
                };

                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.Error("Error getting audio state", ex);
                return Results.Json(new ErrorResponse { Error = "audio_state_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // Set device volume
        _app.MapPost("/api/v1/audio/device", async (HttpContext context) =>
        {
            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetDeviceVolumeRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.DeviceId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_audio_device_request", Details = "Device ID is required" }, statusCode: 400);
                }

                // Validate volume range
                var volume = Math.Clamp(request.Volume, 0f, 1f);

                // Set device volume
                _audioManager.SetDeviceVolume(request.DeviceId, volume, request.Muted);

                // If SetDefault is true, also set as default device
                if (request.SetDefault)
                {
                    try
                    {
                        _audioManager.SetDefaultOutputDevice(request.DeviceId);
                        _logger.Info($"Device {request.DeviceId} set as default output device");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error setting default device: {request.DeviceId}", ex);
                        return Results.Json(new ErrorResponse { Error = "default_device_change_failed", Details = ex.Message }, statusCode: 500);
                    }
                }

                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.Warn($"Invalid audio device request: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "invalid_audio_device_request", Details = ex.Message }, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.Error("Error setting device volume", ex);
                return Results.Json(new ErrorResponse { Error = "audio_device_operation_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // Set session volume
        _app.MapPost("/api/v1/audio/session", async (HttpContext context) =>
        {
            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetSessionVolumeRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.SessionId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_audio_session_request", Details = "Session ID is required" }, statusCode: 400);
                }

                // Validate volume range
                var volume = Math.Clamp(request.Volume, 0f, 1f);

                // Set session volume
                _audioManager.SetSessionVolume(request.SessionId, volume, request.Muted);

                // If OutputDeviceId is provided, route session to that device
                if (!string.IsNullOrEmpty(request.OutputDeviceId))
                {
                    try
                    {
                        _audioManager.SetSessionOutputDevice(request.SessionId, request.OutputDeviceId);
                        _logger.Info($"Session {request.SessionId} routed to device {request.OutputDeviceId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error routing session {request.SessionId} to device {request.OutputDeviceId}", ex);
                        return Results.Json(new ErrorResponse { Error = "session_device_change_failed", Details = ex.Message }, statusCode: 500);
                    }
                }

                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.Warn($"Invalid audio session request: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "invalid_audio_session_request", Details = ex.Message }, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.Error("Error setting session volume", ex);
                return Results.Json(new ErrorResponse { Error = "audio_session_operation_failed", Details = ex.Message }, statusCode: 500);
            }
        });

        // WebSocket endpoint
        _app.Map("/ws/desktop", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            if (_securityManager == null || _sessionBroker == null || _remoteDesktopEngine == null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            // Parse query parameters
            var query = context.Request.Query;
            var sessionId = query["sess"].ToString();
            var token = query["token"].ToString();

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 400;
                return;
            }

            // Validate token
            if (!_securityManager.TryValidateDesktopSessionToken(token, out var validatedToken))
            {
                context.Response.StatusCode = 401;
                return;
            }

            // Verify session exists and matches token
            if (!_sessionBroker.TryGetSession(sessionId, out var session) || session.HaId != validatedToken.HaId)
            {
                context.Response.StatusCode = 404;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            
            // Get current config to avoid stale values
            var currentConfig = _configManager.GetConfig();
            var handler = new DesktopWebSocketHandler(
                webSocket,
                _configManager,
                _securityManager,
                _sessionBroker,
                _remoteDesktopEngine,
                _logger,
                sessionId,
                currentConfig.AgentId);

            await handler.HandleAsync();
        });

        // Start the server asynchronously (fire-and-forget with error handling)
        _runTask = _app.RunAsync();
        _runTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                _logger.Error("API server encountered an error", task.Exception);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);

        _logger.Info($"API server listening on port {config.HttpPort}");
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
        
        // Dispose certificate when server stops
        _certificate?.Dispose();
        _certificate = null;
    }

    private static string DecryptCertPassword(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
        {
            return "";
        }

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedPassword);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            // If decryption fails, return as-is (might be plain text for development)
            return encryptedPassword;
        }
    }
}

