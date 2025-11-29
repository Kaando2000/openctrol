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
                return Results.StatusCode(503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<CreateDesktopSessionRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.HaId))
                {
                    return Results.BadRequest();
                }

                var ttl = TimeSpan.FromSeconds(request.TtlSeconds);
                var token = _securityManager.IssueDesktopSessionToken(request.HaId, ttl);
                var session = _sessionBroker.StartDesktopSession(request.HaId, ttl);

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
                return Results.StatusCode(500);
            }
        });

        // End desktop session
        _app.MapPost("/api/v1/sessions/desktop/{id}/end", (string id) =>
        {
            if (_sessionBroker == null)
            {
                return Results.StatusCode(503);
            }

            _sessionBroker.EndSession(id);
            return Results.Ok();
        });

        // Power control
        _app.MapPost("/api/v1/power", async (HttpContext context) =>
        {
            if (_powerManager == null)
            {
                return Results.StatusCode(503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<PowerRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    return Results.BadRequest();
                }

                switch (request.Action.ToLower())
                {
                    case "restart":
                        _powerManager.Restart();
                        return Results.Ok();
                    case "shutdown":
                        _powerManager.Shutdown();
                        return Results.Ok();
                    default:
                        return Results.BadRequest();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error handling power request", ex);
                return Results.StatusCode(500);
            }
        });

        // Audio state
        _app.MapGet("/api/v1/audio/state", () =>
        {
            if (_audioManager == null)
            {
                return Results.StatusCode(503);
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
                        Muted = s.Muted
                    }).ToList()
                };

                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.Error("Error getting audio state", ex);
                return Results.StatusCode(500);
            }
        });

        // Set device volume
        _app.MapPost("/api/v1/audio/device", async (HttpContext context) =>
        {
            if (_audioManager == null)
            {
                return Results.StatusCode(503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetDeviceVolumeRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.DeviceId))
                {
                    return Results.BadRequest();
                }

                _audioManager.SetDeviceVolume(request.DeviceId, request.Volume, request.Muted);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("Error setting device volume", ex);
                return Results.StatusCode(500);
            }
        });

        // Set session volume
        _app.MapPost("/api/v1/audio/session", async (HttpContext context) =>
        {
            if (_audioManager == null)
            {
                return Results.StatusCode(503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetSessionVolumeRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.SessionId))
                {
                    return Results.BadRequest();
                }

                _audioManager.SetSessionVolume(request.SessionId, request.Volume, request.Muted);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("Error setting session volume", ex);
                return Results.StatusCode(500);
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

