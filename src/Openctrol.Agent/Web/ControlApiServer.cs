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
using Openctrol.Agent.SystemState;
using Openctrol.Agent.Web.Dtos;
using System.Net;
using System.ServiceProcess;
using System.Diagnostics;
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
    private readonly IUptimeService _uptimeService;
    private readonly ISystemStateMonitor? _systemStateMonitor;
    private WebApplication? _app;
    private Task? _runTask;
    private X509Certificate2? _certificate; // Store to prevent disposal before Kestrel uses it

    public ControlApiServer(
        IConfigManager configManager,
        ILogger logger,
        IUptimeService uptimeService,
        ISecurityManager? securityManager = null,
        ISessionBroker? sessionBroker = null,
        IRemoteDesktopEngine? remoteDesktopEngine = null,
        IPowerManager? powerManager = null,
        IAudioManager? audioManager = null,
        ISystemStateMonitor? systemStateMonitor = null)
    {
        _configManager = configManager;
        _logger = logger;
        _uptimeService = uptimeService;
        _securityManager = securityManager;
        _sessionBroker = sessionBroker;
        _remoteDesktopEngine = remoteDesktopEngine;
        _powerManager = powerManager;
        _audioManager = audioManager;
        _systemStateMonitor = systemStateMonitor;
    }

    public void Start()
    {
        StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configManager.GetConfig();
        try
        {
            _logger.Info($"[API] Initializing web server on port {config.HttpPort}...");
            
            var builder = WebApplication.CreateBuilder();

            // Disable default URLs - we'll configure Kestrel explicitly
            builder.WebHost.UseUrls(); // Clear any default URLs
            
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
                    catch (InvalidOperationException ex)
                    {
                        // Certificate password decryption failed - this is a fatal error
                        // We do NOT fall back to HTTP as this indicates a security misconfiguration
                        _logger.Error("Fatal: Certificate password decryption failed. HTTPS cannot be enabled. Fix the certificate password encryption in config.", ex);
                        throw; // Fail startup - certificate configuration is invalid
                    }
                    catch (Exception ex)
                    {
                        // Other certificate loading errors (file format, permissions, etc.)
                        _logger.Error("Error loading certificate, falling back to HTTP", ex);
                        _certificate?.Dispose();
                        _certificate = null;
                        options.ListenAnyIP(config.HttpPort);
                        _logger.Warn("Falling back to HTTP mode due to certificate loading error");
                    }
                }
                else
                {
                    // Fallback to HTTP if no certificate configured
                    options.ListenAnyIP(config.HttpPort);
                    _logger.Info($"HTTP mode (no certificate configured). Listening on port {config.HttpPort}");
                }
            });

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            _app = builder.Build();
            _logger.Info("[API] Web application built successfully");
        
            // Enable WebSocket support - required for /ws/desktop endpoint
            _app.UseWebSockets();

            // Serve static UI files
            SetupStaticFiles();

            // UI endpoints (localhost-only)
            SetupUiEndpoints();

            // Health endpoint (no auth required - public health check)
            _app.MapGet("/api/v1/health", () =>
        {
            try
            {
                var config = _configManager.GetConfig();
                var activeSessions = 0;
                RemoteDesktopStatus? remoteDesktopStatus = null;

                try
                {
                    activeSessions = _sessionBroker?.GetActiveSessions().Count ?? 0;
                }
                catch (Exception ex)
                {
                    _logger.Error("[Health] Error getting active sessions", ex);
                }

                try
                {
                    remoteDesktopStatus = _remoteDesktopEngine?.GetStatus();
                }
                catch (Exception ex)
                {
                    _logger.Error("[Health] Error getting remote desktop status", ex);
                }

                // Map state to desktop_state (login_screen, desktop, locked, unknown)
                // Get desktop state from system state monitor if available
                string desktopState = "unknown";
                try
                {
                    var systemState = _systemStateMonitor?.GetCurrent();
                    if (systemState != null)
                    {
                        desktopState = systemState.DesktopState switch
                        {
                            DesktopState.LoginScreen => "login_screen",
                            DesktopState.Desktop => "desktop",
                            DesktopState.Locked => "locked",
                            _ => "unknown"
                        };
                    }
                    else if (remoteDesktopStatus != null)
                    {
                        // Fallback: Extract desktop state from the state string
                        var stateLower = remoteDesktopStatus.State.ToLower();
                        desktopState = stateLower.Contains("login") ? "login_screen" :
                                     stateLower.Contains("lock") ? "locked" :
                                     stateLower.Contains("desktop") ? "desktop" : "unknown";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[Health] Error getting desktop state: {ex.Message}");
                    // Fallback to parsing state string
                    if (remoteDesktopStatus != null)
                    {
                        var stateLower = remoteDesktopStatus.State.ToLower();
                        desktopState = stateLower.Contains("login") ? "login_screen" :
                                     stateLower.Contains("lock") ? "locked" :
                                     stateLower.Contains("desktop") ? "desktop" : "unknown";
                    }
                }

                var remoteDesktop = remoteDesktopStatus != null
                    ? new RemoteDesktopHealthDto
                    {
                        IsRunning = remoteDesktopStatus.IsRunning,
                        LastFrameAt = remoteDesktopStatus.LastFrameAt,
                        State = remoteDesktopStatus.State,
                        DesktopState = desktopState,
                        Degraded = remoteDesktopStatus.IsDegraded
                    }
                    : new RemoteDesktopHealthDto
                    {
                        IsRunning = false,
                        LastFrameAt = DateTimeOffset.MinValue,
                        State = "unknown",
                        DesktopState = "unknown",
                        Degraded = false
                    };

                var uptimeSeconds = _uptimeService.GetUptimeSeconds();
                var health = new HealthDto
                {
                    AgentId = config.AgentId,
                    Version = "1.0.0",
                    UptimeSeconds = uptimeSeconds,
                    RemoteDesktop = remoteDesktop,
                    ActiveSessions = activeSessions
                };

                return Results.Json(health);
            }
            catch (Exception ex)
            {
                _logger.Error("[Health] Error in health endpoint", ex);
                // Return minimal health response on error
                return Results.Json(new HealthDto
                {
                    AgentId = "",
                    Version = "1.0.0",
                    UptimeSeconds = 0,
                    RemoteDesktop = new RemoteDesktopHealthDto
                    {
                        IsRunning = false,
                        LastFrameAt = DateTimeOffset.MinValue,
                        State = "unknown",
                        DesktopState = "unknown",
                        Degraded = false
                    },
                    ActiveSessions = 0
                });
            }
            });

            // Create desktop session
            _app.MapPost("/api/v1/sessions/desktop", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                    _logger.Warn($"[Security] Rejected session creation request from unauthorized HA ID: {request.HaId}");
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
                _logger.Error($"[Session] Error creating desktop session: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to create desktop session");
                return Results.Json(new ErrorResponse { Error = "session_creation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // End desktop session
            _app.MapPost("/api/v1/sessions/desktop/{id}/end", (HttpContext context, string id) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                _logger.Info($"[Session] Desktop session ended: {id}");
                return Results.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error($"[Session] Error ending session {id}: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to end session");
                return Results.Json(new ErrorResponse { Error = "session_end_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Power control
            _app.MapPost("/api/v1/power", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Action is required (restart, shutdown, or wol)" }, statusCode: 400);
                }

                var action = request.Action.ToLower();
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                
                switch (action)
                {
                    case "restart":
                        _logger.Info($"[API] Power action requested: restart (force={request.Force}) from {clientIp}");
                        _powerManager.Restart();
                        _logger.Info($"[API] Power action restart completed");
                        return Results.Json(new PowerResponse { Status = "ok", Action = "restart" });
                    case "shutdown":
                        _logger.Info($"[API] Power action requested: shutdown (force={request.Force}) from {clientIp}");
                        _powerManager.Shutdown();
                        _logger.Info($"[API] Power action shutdown completed");
                        return Results.Json(new PowerResponse { Status = "ok", Action = "shutdown" });
                    case "wol":
                        // Wake-on-LAN is not currently supported
                        _logger.Warn($"[API] Power action 'wol' requested but not supported from {clientIp}");
                        return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Wake-on-LAN (wol) is not supported" }, statusCode: 400);
                    default:
                        return Results.Json(new ErrorResponse { Error = "invalid_request", Details = $"Unknown action: {request.Action}. Must be 'restart', 'shutdown', or 'wol'" }, statusCode: 400);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error handling power request: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to execute power action");
                return Results.Json(new ErrorResponse { Error = "power_operation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Audio state (legacy endpoint, kept for compatibility)
            _app.MapGet("/api/v1/audio/state", (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                _logger.Error($"[Audio] Error getting audio state: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to retrieve audio state");
                return Results.Json(new ErrorResponse { Error = "audio_state_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Audio status (new endpoint for HA integration)
            _app.MapGet("/api/v1/audio/status", (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                _logger.Info("[API] Audio status requested");
                var state = _audioManager.GetState();
                
                // Get master volume from default device
                var defaultDevice = state.Devices.FirstOrDefault(d => d.IsDefault);
                var masterVolume = defaultDevice != null ? defaultDevice.Volume * 100f : 0f; // Convert 0-1 to 0-100
                var masterMuted = defaultDevice?.Muted ?? false;

                var response = new AudioStatusResponse
                {
                    Master = new AudioMasterDto
                    {
                        Volume = masterVolume,
                        Muted = masterMuted
                    },
                    Devices = state.Devices.Select(d => new AudioDeviceInfoDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Volume = d.Volume * 100f, // Convert 0-1 to 0-100
                        Muted = d.Muted,
                        IsDefault = d.IsDefault
                    }).ToList()
                };

                _logger.Info("[API] Audio status retrieved successfully");
                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error getting audio status: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to retrieve audio status");
                return Results.Json(new ErrorResponse { Error = "audio_status_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Set master volume
            _app.MapPost("/api/v1/audio/master", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                _logger.Info("[API] Audio master volume change requested");
                var request = await JsonSerializer.DeserializeAsync<SetMasterVolumeRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Request body is required" }, statusCode: 400);
                }

                var state = _audioManager.GetState();
                var defaultDevice = state.Devices.FirstOrDefault(d => d.IsDefault);
                
                if (defaultDevice == null)
                {
                    return Results.Json(new ErrorResponse { Error = "not_found", Details = "No default audio device found" }, statusCode: 404);
                }

                // Get current values if not provided
                var volume = request.Volume.HasValue 
                    ? Math.Clamp(request.Volume.Value / 100f, 0f, 1f) // Convert 0-100 to 0-1
                    : defaultDevice.Volume;
                var muted = request.Muted ?? defaultDevice.Muted;

                // Validate volume range
                if (request.Volume.HasValue && (request.Volume.Value < 0 || request.Volume.Value > 100))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Volume must be between 0 and 100" }, statusCode: 400);
                }

                // Set device volume (which is the master)
                _audioManager.SetDeviceVolume(defaultDevice.Id, volume, muted);
                _logger.Info($"[API] Audio master volume set to {request.Volume ?? volume * 100}%, muted={muted}");

                return Results.Json(new StatusResponse { Status = "ok" });
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error setting master volume: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set master volume");
                return Results.Json(new ErrorResponse { Error = "audio_master_operation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Set device volume (updated for HA integration)
            _app.MapPost("/api/v1/audio/device", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetDeviceVolumeSimpleRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.DeviceId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Device ID is required" }, statusCode: 400);
                }

                // Check if device exists
                var state = _audioManager.GetState();
                var device = state.Devices.FirstOrDefault(d => d.Id == request.DeviceId);
                if (device == null)
                {
                    _logger.Warn($"[API] Invalid device ID requested: {request.DeviceId}");
                    return Results.Json(new ErrorResponse { Error = "not_found", Details = $"Device ID '{request.DeviceId}' not found" }, statusCode: 404);
                }

                // Get current values if not provided
                var volume = request.Volume.HasValue 
                    ? Math.Clamp(request.Volume.Value / 100f, 0f, 1f) // Convert 0-100 to 0-1
                    : device.Volume;
                var muted = request.Muted ?? device.Muted;

                // Validate volume range
                if (request.Volume.HasValue && (request.Volume.Value < 0 || request.Volume.Value > 100))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Volume must be between 0 and 100" }, statusCode: 400);
                }

                _logger.Info($"[API] Setting device volume: {request.DeviceId}, volume={request.Volume ?? volume * 100}%, muted={muted}");
                _audioManager.SetDeviceVolume(request.DeviceId, volume, muted);
                _logger.Info($"[API] Device volume set successfully");

                return Results.Json(new StatusResponse { Status = "ok" });
            }
            catch (ArgumentException ex)
            {
                _logger.Warn($"[API] Invalid audio device request: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "invalid_request", Details = ex.Message }, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error setting device volume: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set device volume");
                return Results.Json(new ErrorResponse { Error = "audio_device_operation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Set default audio device
            _app.MapPost("/api/v1/audio/default", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_audioManager == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Audio manager not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SetDefaultDeviceRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.DeviceId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Device ID is required" }, statusCode: 400);
                }

                // Check if device exists
                var state = _audioManager.GetState();
                var device = state.Devices.FirstOrDefault(d => d.Id == request.DeviceId);
                if (device == null)
                {
                    _logger.Warn($"[API] Invalid device ID requested for default: {request.DeviceId}");
                    return Results.Json(new ErrorResponse { Error = "not_found", Details = $"Device ID '{request.DeviceId}' not found" }, statusCode: 404);
                }

                _logger.Info($"[API] Setting default audio device: {request.DeviceId}");
                _audioManager.SetDefaultOutputDevice(request.DeviceId);
                _logger.Info($"[API] Default audio device set successfully");

                return Results.Json(new StatusResponse { Status = "ok" });
            }
            catch (NotSupportedException ex)
            {
                _logger.Warn($"[API] Setting default device not supported: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "not_implemented", Details = "Setting default audio device is not supported on this system" }, statusCode: 501);
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error setting default device: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set default audio device");
                return Results.Json(new ErrorResponse { Error = "audio_default_device_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Legacy device volume endpoint (kept for compatibility)
            _app.MapPost("/api/v1/audio/device/legacy", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                        _logger.Info($"[Audio] Device {request.DeviceId} set as default output device");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Audio] Error setting default device {request.DeviceId}: {ex.Message}", ex);
                        var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set default audio device");
                        return Results.Json(new ErrorResponse { Error = "default_device_change_failed", Details = sanitizedMessage }, statusCode: 500);
                    }
                }

                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.Warn($"[Audio] Invalid audio device request: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "invalid_audio_device_request", Details = ex.Message }, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Audio] Error setting device volume: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set device volume");
                return Results.Json(new ErrorResponse { Error = "audio_device_operation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Set session volume
            _app.MapPost("/api/v1/audio/session", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

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
                        _logger.Info($"[Audio] Session {request.SessionId} routed to device {request.OutputDeviceId}");
                    }
                    catch (NotSupportedException ex)
                    {
                        _logger.Warn($"[Audio] Per-app routing not supported for session {request.SessionId}: {ex.Message}");
                        return Results.Json(new ErrorResponse { Error = "per_app_routing_not_supported", Details = ex.Message }, statusCode: 501);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Audio] Error routing session {request.SessionId} to device {request.OutputDeviceId}: {ex.Message}", ex);
                        var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to route session to device");
                        return Results.Json(new ErrorResponse { Error = "session_device_change_failed", Details = sanitizedMessage }, statusCode: 500);
                    }
                }

                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                _logger.Warn($"[Audio] Invalid audio session request: {ex.Message}");
                return Results.Json(new ErrorResponse { Error = "invalid_audio_session_request", Details = ex.Message }, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Audio] Error setting session volume: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to set session volume");
                return Results.Json(new ErrorResponse { Error = "audio_session_operation_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Get monitors
            _app.MapGet("/api/v1/rd/monitors", (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_remoteDesktopEngine == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Remote desktop engine not available" }, statusCode: 503);
            }

            try
            {
                _logger.Info("[API] Monitors enumeration requested");
                var monitors = _remoteDesktopEngine.GetMonitors();
                var currentMonitorId = _remoteDesktopEngine.GetCurrentMonitorId();

                var response = new MonitorsResponse
                {
                    Monitors = monitors.Select(m => new MonitorInfoDto
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Resolution = $"{m.Width}x{m.Height}",
                        Width = m.Width,
                        Height = m.Height,
                        IsPrimary = m.IsPrimary
                    }).ToList(),
                    CurrentMonitorId = currentMonitorId
                };

                _logger.Info($"[API] Monitors enumeration returned {monitors.Count} monitors");
                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error enumerating monitors: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to enumerate monitors");
                return Results.Json(new ErrorResponse { Error = "monitors_enumeration_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // Select monitor
            _app.MapPost("/api/v1/rd/monitor", async (HttpContext context) =>
        {
            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                return authResult;
            }

            if (_remoteDesktopEngine == null)
            {
                return Results.Json(new ErrorResponse { Error = "service_unavailable", Details = "Remote desktop engine not available" }, statusCode: 503);
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<SelectMonitorRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.MonitorId))
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Monitor ID is required" }, statusCode: 400);
                }

                // Validate monitor exists
                var monitors = _remoteDesktopEngine.GetMonitors();
                var monitor = monitors.FirstOrDefault(m => m.Id == request.MonitorId);
                if (monitor == null)
                {
                    _logger.Warn($"[API] Invalid monitor ID requested: {request.MonitorId}");
                    return Results.Json(new ErrorResponse { Error = "not_found", Details = $"Monitor ID '{request.MonitorId}' not found" }, statusCode: 404);
                }

                _logger.Info($"[API] Selecting monitor: {request.MonitorId}");
                _remoteDesktopEngine.SelectMonitor(request.MonitorId);
                _logger.Info($"[API] Monitor selected successfully");

                return Results.Json(new SelectMonitorResponse { Status = "ok", MonitorId = request.MonitorId });
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error selecting monitor: {ex.Message}", ex);
                var sanitizedMessage = SanitizeErrorMessage(ex, "Failed to select monitor");
                return Results.Json(new ErrorResponse { Error = "monitor_selection_failed", Details = sanitizedMessage }, statusCode: 500);
            }
        });

            // WebSocket endpoint for input (HA remote control)
            _app.Map("/api/v1/rd/session", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "invalid_request", Details = "WebSocket upgrade required" });
                return;
            }

            // Check authentication
            var authResult = CheckAuthentication(context);
            if (authResult != null)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "unauthorized", Details = "Missing or invalid credentials" });
                return;
            }

            if (_remoteDesktopEngine == null)
            {
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "service_unavailable", Details = "Remote desktop engine not available" });
                return;
            }

            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.Info($"[API] WebSocket input connection opened from {clientIp}");

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[4096];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageText = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                        
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(messageText);
                            if (!jsonDoc.RootElement.TryGetProperty("type", out var typeElement))
                            {
                                _logger.Warn($"[API] Invalid WebSocket message: missing 'type' field");
                                await SendWebSocketError(webSocket, "Invalid message: missing 'type' field");
                                continue;
                            }

                            var messageType = typeElement.GetString();
                            
                            if (messageType == "pointer")
                            {
                                await HandlePointerMessage(jsonDoc, _remoteDesktopEngine);
                            }
                            else if (messageType == "keyboard")
                            {
                                await HandleKeyboardMessage(jsonDoc, _remoteDesktopEngine);
                            }
                            else
                            {
                                _logger.Warn($"[API] Unknown WebSocket message type: {messageType}");
                                await SendWebSocketError(webSocket, $"Unknown message type: {messageType}");
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.Warn($"[API] Invalid JSON in WebSocket message: {ex.Message}");
                            await SendWebSocketError(webSocket, "Invalid JSON format");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"[API] Error processing WebSocket message: {ex.Message}", ex);
                            await SendWebSocketError(webSocket, "Error processing message");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] WebSocket error: {ex.Message}", ex);
            }
            finally
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Server error", CancellationToken.None);
                    }
                    catch { }
                }
                _logger.Info($"[API] WebSocket input connection closed from {clientIp}");
            }
        });

            // WebSocket endpoint (legacy desktop session)
            _app.Map("/ws/desktop", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "invalid_request", Details = "WebSocket upgrade required" });
                return;
            }

            if (_securityManager == null || _sessionBroker == null || _remoteDesktopEngine == null)
            {
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "service_unavailable", Details = "Required services not available" });
                return;
            }

            // SECURITY NOTE: Session token is passed via query string for compatibility.
            // Tokens in URLs are visible in:
            //   - Server access logs
            //   - Browser history
            //   - Proxy logs
            //   - Referrer headers
            // 
            // Mitigations:
            //   - Always use HTTPS/TLS in production to encrypt URLs in transit
            //   - Tokens have TTL and are single-use per session
            //   - Server logs should not include full URLs with query strings (redact if logging)
            // 
            // Future enhancement: Consider moving token to WebSocket subprotocol or initial message
            // after upgrade for better security.
            
            // Parse query parameters
            var query = context.Request.Query;
            var sessionId = query["sess"].ToString();
            var token = query["token"].ToString();
            
            // Log connection attempt without exposing token (for security)
            _logger.Debug($"[WebSocket] Connection attempt for session {sessionId} from {context.Connection.RemoteIpAddress}");

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "invalid_request", Details = "Session ID and token are required" });
                return;
            }

            // Validate token
            if (!_securityManager.TryValidateDesktopSessionToken(token, out var validatedToken))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "unauthorized", Details = "Invalid or expired session token" });
                return;
            }

            // Verify session exists and matches token
            if (!_sessionBroker.TryGetSession(sessionId, out var session) || session.HaId != validatedToken.HaId)
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "not_found", Details = "Session not found or does not match token" });
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

            // Start the server asynchronously and observe failures
            _logger.Info($"[API] Starting Kestrel server on port {config.HttpPort}...");
            
            // Write directly to Event Log immediately (before async operations)
            try
            {
                System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                    $"[API] Starting web server on port {config.HttpPort}...", 
                    System.Diagnostics.EventLogEntryType.Information);
            }
            catch { }
            
            try
            {
                _runTask = _app.RunAsync(cancellationToken);
            }
            catch (Exception startEx)
            {
                var errorMsg = $"[API] CRITICAL: RunAsync() threw exception immediately: {startEx.Message}";
                _logger.Error(errorMsg, startEx);
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                        $"{errorMsg}\nType: {startEx.GetType().Name}\nStack: {startEx.StackTrace}", 
                        System.Diagnostics.EventLogEntryType.Error);
                }
                catch { }
                throw;
            }
            
            // Set up error handler for async failures (fire-and-forget)
            _ = _runTask.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    var ex = task.Exception?.GetBaseException();
                    var errorMsg = $"[API] Server encountered a fatal error on port {config.HttpPort}: {ex?.Message}";
                    _logger.Error(errorMsg, ex);
                    
                    // Also write to Event Log as fallback
                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                            $"{errorMsg}\nType: {ex?.GetType().Name}\nStack: {ex?.StackTrace}", 
                            System.Diagnostics.EventLogEntryType.Error);
                    }
                    catch { }
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Wait for server to start binding (async wait with timeout)
            // This helps catch immediate binding failures
            var startTimeout = TimeSpan.FromSeconds(10);
            var startTime = DateTime.UtcNow;
            var portListening = false;
            var protocol = !string.IsNullOrEmpty(config.CertPath) && File.Exists(config.CertPath) ? "https" : "http";
            
            _logger.Info($"[API] Waiting for server to bind to {protocol}://localhost:{config.HttpPort}...");
            
            while (DateTime.UtcNow - startTime < startTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if task has faulted
                if (_runTask != null && _runTask.IsFaulted)
                {
                    var ex = _runTask.Exception?.GetBaseException();
                    var errorMsg = $"[BOOT] [ERROR] HTTP API failed to start on port {config.HttpPort}: {ex?.Message}";
                    _logger.Error(errorMsg, ex);
                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                            $"{errorMsg}\nType: {ex?.GetType().Name}\nStack: {ex?.StackTrace}", 
                            System.Diagnostics.EventLogEntryType.Error);
                    }
                    catch { }
                    throw new InvalidOperationException($"Failed to start API server on port {config.HttpPort}", ex);
                }
                
                // Check if port is listening by trying to connect (async)
                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                    
                    var connectTask = client.ConnectAsync(IPAddress.Loopback, config.HttpPort);
                    var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                    
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    
                    if (completedTask == connectTask && client.Connected)
                    {
                        client.Close();
                        portListening = true;
                        break;
                    }
                }
                catch
                {
                    // Port not ready yet, continue waiting
                }
                
                // Small delay before next check (async)
                await Task.Delay(500, cancellationToken);
            }
            
            // Final check for errors
            if (_runTask != null && _runTask.IsFaulted)
            {
                var ex = _runTask.Exception?.GetBaseException();
                var errorMsg = $"[BOOT] [ERROR] HTTP API failed to start on port {config.HttpPort}: {ex?.Message}";
                _logger.Error(errorMsg, ex);
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                        $"{errorMsg}\nType: {ex?.GetType().Name}\nStack: {ex?.StackTrace}", 
                        System.Diagnostics.EventLogEntryType.Error);
                }
                catch { }
                throw new InvalidOperationException($"Failed to start API server on port {config.HttpPort}", ex);
            }
            
            if (portListening)
            {
                _logger.Info($"[BOOT] HTTP API server started successfully on {protocol}://localhost:{config.HttpPort} (UseHttps={protocol == "https"})");
                _logger.Info($"[API] Health endpoint: {protocol}://localhost:{config.HttpPort}/api/v1/health");
                _logger.Info($"[API] UI endpoint: {protocol}://localhost:{config.HttpPort}/ui");
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", 
                        $"[BOOT] HTTP API server started successfully on {protocol}://localhost:{config.HttpPort}\nHealth: {protocol}://localhost:{config.HttpPort}/api/v1/health\nUI: {protocol}://localhost:{config.HttpPort}/ui", 
                        System.Diagnostics.EventLogEntryType.Information);
                }
                catch { }
            }
            else
            {
                // Port not listening after timeout - this is a fatal error
                var errorMsg = $"[BOOT] [ERROR] HTTP API server task started but port {config.HttpPort} is not listening after {startTimeout.TotalSeconds}s timeout. This indicates a binding failure.";
                _logger.Error(errorMsg);
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", errorMsg, System.Diagnostics.EventLogEntryType.Error);
                }
                catch { }
                throw new InvalidOperationException($"API server failed to bind to port {config.HttpPort} within {startTimeout.TotalSeconds} seconds");
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"[API] Failed to start server on port {config.HttpPort}: {ex.Message}";
            _logger.Error(errorMsg, ex);
            
            // Also write to Event Log as fallback
            try
            {
                System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", errorMsg, System.Diagnostics.EventLogEntryType.Error);
            }
            catch { }
            
            throw;
        }
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

    private IResult? CheckAuthentication(HttpContext context)
    {
        var config = _configManager.GetConfig();
        
            // SECURITY WARNING: If no API key is configured, authentication is disabled.
            // This is acceptable ONLY for development/testing. In production, always configure ApiKey.
            // Empty ApiKey means all REST endpoints are accessible without authentication.
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            // Log warning on first unauthenticated request to sensitive endpoint
            // (We don't want to spam logs, so this is a one-time warning per endpoint pattern)
            _logger.Warn($"[Auth] SECURITY WARNING: API key not configured. Authentication is DISABLED. " +
                        $"This endpoint ({context.Request.Path}) is accessible without authentication. " +
                        $"Configure ApiKey in config.json for production use.");
            return null; // No auth required (development mode)
        }

            // Check for API key in header (X-Openctrol-Key or Authorization: Bearer <key>)
        string? providedKey = null;
        
            // Try X-Openctrol-Key header first
        if (context.Request.Headers.TryGetValue("X-Openctrol-Key", out var headerValue))
        {
            providedKey = headerValue.ToString();
        }
            // Try Authorization: Bearer <key>
        else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authValue = authHeader.ToString();
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authValue.Substring(7).Trim();
            }
        }

        if (string.IsNullOrEmpty(providedKey))
        {
            _logger.Warn($"[Auth] Unauthenticated request to {context.Request.Path} from {context.Connection.RemoteIpAddress}");
            return Results.Json(new ErrorResponse { Error = "unauthorized", Details = "Missing or invalid credentials" }, statusCode: 401);
        }

            // Use constant-time comparison to prevent timing attacks
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(config.ApiKey),
            System.Text.Encoding.UTF8.GetBytes(providedKey)))
        {
            _logger.Warn($"[Auth] Invalid API key provided for {context.Request.Path} from {context.Connection.RemoteIpAddress}");
            return Results.Json(new ErrorResponse { Error = "unauthorized", Details = "Missing or invalid credentials" }, statusCode: 401);
        }

        return null; // Authentication successful
    }

    /// <summary>
    /// Sanitizes exception messages for client responses.
    /// Full exception details are logged server-side, but client responses should be generic.
    /// </summary>
    private static string SanitizeErrorMessage(Exception ex, string defaultMessage)
    {
            // For ArgumentException, InvalidOperationException, etc., use the message as-is
            // (these are usually safe and informative for clients)
        if (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
        {
            return ex.Message;
        }
        
            // For other exceptions, return generic message to avoid leaking internal details
        return defaultMessage;
    }

    /// <summary>
    /// Decrypts a certificate password that was encrypted using DPAPI (Data Protection API).
    /// Certificate passwords must be stored encrypted in the config file for security.
    /// This method does NOT fall back to plain text - decryption failures are fatal.
    /// </summary>
    /// <param name="encryptedPassword">Base64-encoded encrypted password</param>
    /// <returns>Decrypted password string</returns>
    /// <exception cref="InvalidOperationException">If decryption fails or password format is invalid</exception>
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
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Certificate password is not in valid Base64 format. Passwords must be encrypted using DPAPI before storing in config.", ex);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Failed to decrypt certificate password. The password may have been encrypted on a different machine or the encryption key is unavailable. Re-encrypt the password on this machine.", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unexpected error decrypting certificate password. Ensure the password was encrypted using DPAPI (DataProtectionScope.LocalMachine).", ex);
        }
    }

    /// <summary>
    /// Checks if the request is from localhost (127.0.0.1 or ::1).
    /// Returns null if localhost, or an error result if not.
    /// </summary>
    private Task HandlePointerMessage(JsonDocument jsonDoc, IRemoteDesktopEngine remoteDesktopEngine)
    {
        var root = jsonDoc.RootElement;
        if (!root.TryGetProperty("event", out var eventElement))
        {
            _logger.Warn("[API] Pointer message missing 'event' field");
            return Task.CompletedTask;
        }

        var eventType = eventElement.GetString();
        
        switch (eventType)
        {
            case "move":
                if (root.TryGetProperty("dx", out var dxElement) && root.TryGetProperty("dy", out var dyElement))
                {
                    var dx = (int)Math.Round(dxElement.GetSingle());
                    var dy = (int)Math.Round(dyElement.GetSingle());
                    var evt = new Openctrol.Agent.Input.PointerEvent
                    {
                        Kind = Openctrol.Agent.Input.PointerEventKind.MoveRelative,
                        Dx = dx,
                        Dy = dy
                    };
                    remoteDesktopEngine.InjectPointer(evt);
                }
                break;

            case "click":
                if (root.TryGetProperty("button", out var buttonElement))
                {
                    var buttonStr = buttonElement.GetString();
                    var button = buttonStr?.ToLower() switch
                    {
                        "left" => Openctrol.Agent.Input.MouseButton.Left,
                        "right" => Openctrol.Agent.Input.MouseButton.Right,
                        "middle" => Openctrol.Agent.Input.MouseButton.Middle,
                        _ => (Openctrol.Agent.Input.MouseButton?)null
                    };

                    if (button.HasValue)
                    {
                        var evt = new Openctrol.Agent.Input.PointerEvent
                        {
                            Kind = Openctrol.Agent.Input.PointerEventKind.Button,
                            Button = button.Value,
                            ButtonAction = Openctrol.Agent.Input.MouseButtonAction.Down
                        };
                        remoteDesktopEngine.InjectPointer(evt);
                        
                        // Also send button up
                        var evtUp = new Openctrol.Agent.Input.PointerEvent
                        {
                            Kind = Openctrol.Agent.Input.PointerEventKind.Button,
                            Button = button.Value,
                            ButtonAction = Openctrol.Agent.Input.MouseButtonAction.Up
                        };
                        remoteDesktopEngine.InjectPointer(evtUp);
                    }
                }
                break;

            case "scroll":
                if (root.TryGetProperty("dx", out var scrollDxElement) && root.TryGetProperty("dy", out var scrollDyElement))
                {
                    var scrollDx = (int)Math.Round(scrollDxElement.GetSingle());
                    var scrollDy = (int)Math.Round(scrollDyElement.GetSingle());
                    var evt = new Openctrol.Agent.Input.PointerEvent
                    {
                        Kind = Openctrol.Agent.Input.PointerEventKind.Wheel,
                        WheelDeltaX = scrollDx,
                        WheelDeltaY = scrollDy
                    };
                    remoteDesktopEngine.InjectPointer(evt);
                }
                break;

            default:
                _logger.Warn($"[API] Unknown pointer event type: {eventType}");
                break;
        }
        
        return Task.CompletedTask;
    }

    private Task HandleKeyboardMessage(JsonDocument jsonDoc, IRemoteDesktopEngine remoteDesktopEngine)
    {
        var root = jsonDoc.RootElement;
        if (!root.TryGetProperty("keys", out var keysElement))
        {
            _logger.Warn("[API] Keyboard message missing 'keys' field");
            return Task.CompletedTask;
        }

        var keys = keysElement.EnumerateArray().Select(k => k.GetString() ?? "").ToList();
        if (keys.Count == 0)
        {
            _logger.Warn("[API] Keyboard message has empty 'keys' array");
            return Task.CompletedTask;
        }

        // Parse modifiers and main keys
        var modifiers = Openctrol.Agent.Input.KeyModifiers.None;
        var mainKeys = new List<int>();

        foreach (var keyStr in keys)
        {
            var keyUpper = keyStr.ToUpperInvariant();
            switch (keyUpper)
            {
                case "CTRL":
                    modifiers |= Openctrol.Agent.Input.KeyModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= Openctrol.Agent.Input.KeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= Openctrol.Agent.Input.KeyModifiers.Shift;
                    break;
                case "WIN":
                    modifiers |= Openctrol.Agent.Input.KeyModifiers.Win;
                    break;
                default:
                    // Try to map to key code
                    var keyCode = MapKeyNameToCode(keyUpper);
                    if (keyCode.HasValue)
                    {
                        mainKeys.Add(keyCode.Value);
                    }
                    else
                    {
                        _logger.Warn($"[API] Unknown key name: {keyStr}");
                    }
                    break;
            }
        }

        // Send key down for all keys
        foreach (var keyCode in mainKeys)
        {
            var evt = new Openctrol.Agent.Input.KeyboardEvent
            {
                Kind = Openctrol.Agent.Input.KeyboardEventKind.KeyDown,
                KeyCode = keyCode,
                Modifiers = modifiers
            };
            remoteDesktopEngine.InjectKey(evt);
        }

        // Send key up for all keys (in reverse order)
        foreach (var keyCode in mainKeys.Reverse<int>())
        {
            var evt = new Openctrol.Agent.Input.KeyboardEvent
            {
                Kind = Openctrol.Agent.Input.KeyboardEventKind.KeyUp,
                KeyCode = keyCode,
                Modifiers = modifiers
            };
            remoteDesktopEngine.InjectKey(evt);
        }

        // Release modifiers
        if (modifiers != Openctrol.Agent.Input.KeyModifiers.None)
        {
            // Modifiers are released automatically by InputDispatcher
        }
        
        return Task.CompletedTask;
    }

    private int? MapKeyNameToCode(string keyName)
    {
        // Map common key names to virtual key codes
        return keyName switch
        {
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" => 0x1B,
            "SPACE" => 0x20,
            "BACKSPACE" => 0x08,
            "DEL" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => null
        };
    }

    private async Task SendWebSocketError(WebSocket webSocket, string message)
    {
        try
        {
            var error = new ErrorMessage { Type = "error", Message = message };
            var json = JsonSerializer.Serialize(error);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error($"[API] Error sending WebSocket error message: {ex.Message}", ex);
        }
    }

    private static IResult? CheckLocalhostOnly(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        
        if (remoteIp == null)
        {
            return Results.Json(new ErrorResponse { Error = "forbidden", Details = "Could not determine client IP address" }, statusCode: 403);
        }

            // Check for IPv4 loopback (127.0.0.1) or IPv6 loopback (::1)
        if (!IPAddress.IsLoopback(remoteIp))
        {
            return Results.Json(new ErrorResponse { Error = "forbidden", Details = "UI endpoints are only accessible from localhost" }, statusCode: 403);
        }

        return null; // Request is from localhost
    }

    /// <summary>
    /// Sets up static file serving for the UI.
    /// </summary>
    private void SetupStaticFiles()
    {
        if (_app == null) return;

            // Serve UI HTML at /ui
            _app.MapGet("/ui", (HttpContext context) =>
        {
            // Check localhost-only
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            var html = GetUiHtml();
            return Results.Content(html, "text/html");
        });

            // Serve UI JavaScript at /ui/app.js
            _app.MapGet("/ui/app.js", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            var js = GetUiJavaScript();
            return Results.Content(js, "application/javascript");
        });
    }

    /// <summary>
    /// Sets up UI API endpoints.
    /// </summary>
    private void SetupUiEndpoints()
    {
        if (_app == null) return;

            // GET /api/v1/ui/status - Get aggregated status
            _app.MapGet("/api/v1/ui/status", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            try
            {
                var config = _configManager.GetConfig();
                var uptimeSeconds = _uptimeService.GetUptimeSeconds();
                
                // Get service status
                var serviceStatus = GetServiceStatus();
                
                // Get health info
                var desktopState = "unknown";
                var isDegraded = false;
                var activeSessions = 0;
                
                try
                {
                    var systemState = _systemStateMonitor?.GetCurrent();
                    if (systemState != null)
                    {
                        desktopState = systemState.DesktopState switch
                        {
                            DesktopState.LoginScreen => "login_screen",
                            DesktopState.Desktop => "desktop",
                            DesktopState.Locked => "locked",
                            _ => "unknown"
                        };
                    }
                    
                    var remoteDesktopStatus = _remoteDesktopEngine?.GetStatus();
                    if (remoteDesktopStatus != null)
                    {
                        isDegraded = remoteDesktopStatus.IsDegraded;
                    }
                    
                    activeSessions = _sessionBroker?.GetActiveSessions().Count ?? 0;
                }
                catch (Exception ex)
                {
                    _logger.Debug($"[UI] Error getting health info: {ex.Message}");
                }

                var status = new UiStatusDto
                {
                    Agent = new AgentInfoDto
                    {
                        AgentId = config.AgentId,
                        Version = "1.0.0",
                        UptimeSeconds = uptimeSeconds
                    },
                    Service = serviceStatus,
                    Health = new HealthInfoDto
                    {
                        DesktopState = desktopState,
                        IsDegraded = isDegraded,
                        ActiveSessions = activeSessions
                    },
                    Config = new ConfigSummaryDto
                    {
                        Port = config.HttpPort,
                        UseHttps = !string.IsNullOrEmpty(config.CertPath),
                        ApiKeyConfigured = !string.IsNullOrEmpty(config.ApiKey),
                        AllowedHaIds = config.AllowedHaIds?.ToList() ?? new List<string>()
                    }
                };

                return Results.Json(status);
            }
            catch (Exception ex)
            {
                _logger.Error("[UI] Error in status endpoint", ex);
                return Results.Json(new ErrorResponse { Error = "internal_error", Details = "Failed to get status" }, statusCode: 500);
            }
        });

            // GET /api/v1/ui/config - Get config (sanitized)
            _app.MapGet("/api/v1/ui/config", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            try
            {
                var config = _configManager.GetConfig();
                var uiConfig = new UiConfigDto
                {
                    Port = config.HttpPort,
                    UseHttps = !string.IsNullOrEmpty(config.CertPath),
                    CertPath = config.CertPath ?? "",
                    ApiKeyConfigured = !string.IsNullOrEmpty(config.ApiKey),
                    AllowedHaIds = config.AllowedHaIds?.ToList() ?? new List<string>(),
                    AllowEmptyApiKey = string.IsNullOrEmpty(config.ApiKey), // If empty, it's allowed
                    RequireAuthForHealth = false // Health endpoint doesn't require auth currently
                };

                return Results.Json(uiConfig);
            }
            catch (Exception ex)
            {
                _logger.Error("[UI] Error getting config", ex);
                return Results.Json(new ErrorResponse { Error = "internal_error", Details = "Failed to get config" }, statusCode: 500);
            }
        });

            // POST /api/v1/ui/config - Update config
            _app.MapPost("/api/v1/ui/config", async (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            try
            {
                var request = await JsonSerializer.DeserializeAsync<UiConfigUpdateRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_request", Details = "Request body is required" }, statusCode: 400);
                }

                var currentConfig = _configManager.GetConfig();
                
                // Validate port
                if (request.Port.HasValue)
                {
                    if (request.Port.Value < 1 || request.Port.Value > 65535)
                    {
                        return Results.Json(new ErrorResponse { Error = "invalid_port", Details = "Port must be between 1 and 65535" }, statusCode: 400);
                    }
                    currentConfig.HttpPort = request.Port.Value;
                }

                // Validate HTTPS config
                if (request.UseHttps.HasValue && request.UseHttps.Value)
                {
                    var certPath = request.CertPath ?? currentConfig.CertPath;
                    if (string.IsNullOrEmpty(certPath))
                    {
                        return Results.Json(new ErrorResponse { Error = "invalid_cert", Details = "Certificate path is required when HTTPS is enabled" }, statusCode: 400);
                    }
                    if (!File.Exists(certPath))
                    {
                        return Results.Json(new ErrorResponse { Error = "cert_not_found", Details = $"Certificate file not found: {certPath}" }, statusCode: 400);
                    }
                    currentConfig.CertPath = certPath;
                }
                else if (request.UseHttps.HasValue && !request.UseHttps.Value)
                {
                    currentConfig.CertPath = "";
                    currentConfig.CertPasswordEncrypted = "";
                }

                // Update API key if provided
                if (request.ApiKey != null)
                {
                    if (string.IsNullOrEmpty(request.ApiKey) && !(request.AllowEmptyApiKey ?? false))
                    {
                        // Check if we already have an API key
                        if (string.IsNullOrEmpty(currentConfig.ApiKey))
                        {
                            return Results.Json(new ErrorResponse { Error = "api_key_required", Details = "API key is required. Set allowEmptyApiKey to true to allow empty API key." }, statusCode: 400);
                        }
                        // If empty and we have existing key, keep it
                    }
                    else if (!string.IsNullOrEmpty(request.ApiKey))
                    {
                        currentConfig.ApiKey = request.ApiKey;
                    }
                }

                // Update allowed HA IDs
                if (request.AllowedHaIds != null)
                {
                    currentConfig.AllowedHaIds = request.AllowedHaIds;
                }

                // Ensure AgentId is set
                if (string.IsNullOrEmpty(currentConfig.AgentId))
                {
                    currentConfig.AgentId = Guid.NewGuid().ToString();
                }

                // Ensure AllowedHaIds is not null
                if (currentConfig.AllowedHaIds == null)
                {
                    currentConfig.AllowedHaIds = new List<string>();
                }

                // Validate the updated config before saving
                // Basic validation
                if (currentConfig.HttpPort < 1 || currentConfig.HttpPort > 65535)
                {
                    return Results.Json(new ErrorResponse { Error = "invalid_port", Details = "Port must be between 1 and 65535" }, statusCode: 400);
                }

                if (!string.IsNullOrEmpty(currentConfig.CertPath) && !File.Exists(currentConfig.CertPath))
                {
                    return Results.Json(new ErrorResponse { Error = "cert_not_found", Details = $"Certificate file not found: {currentConfig.CertPath}" }, statusCode: 400);
                }

                // Save config
                try
                {
                    _configManager.SaveConfig(currentConfig);
                    _logger.Info("[UI] Config updated via UI");
                    return Results.Json(new { success = true, message = "Config updated. Please restart the Openctrol Agent service to apply changes." });
                }
                catch (InvalidOperationException ex)
                {
                    _logger.Warn($"[UI] Config validation failed: {ex.Message}");
                    return Results.Json(new ErrorResponse { Error = "validation_failed", Details = ex.Message }, statusCode: 400);
                }
                catch (Exception ex)
                {
                    _logger.Error("[UI] Error saving config", ex);
                    return Results.Json(new ErrorResponse { Error = "save_failed", Details = SanitizeErrorMessage(ex, "Failed to save config") }, statusCode: 500);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[UI] Error updating config", ex);
                return Results.Json(new ErrorResponse { Error = "internal_error", Details = SanitizeErrorMessage(ex, "Failed to update config") }, statusCode: 500);
            }
        });

            // POST /api/v1/ui/service/stop
            _app.MapPost("/api/v1/ui/service/stop", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            return ControlService("stop");
        });

            // POST /api/v1/ui/service/start
            _app.MapPost("/api/v1/ui/service/start", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            return ControlService("start");
        });

            // POST /api/v1/ui/service/restart
            _app.MapPost("/api/v1/ui/service/restart", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            return ControlService("restart");
        });

            // POST /api/v1/ui/service/uninstall
            _app.MapPost("/api/v1/ui/service/uninstall", (HttpContext context) =>
        {
            var localhostCheck = CheckLocalhostOnly(context);
            if (localhostCheck != null)
            {
                return localhostCheck;
            }

            try
            {
                // Find uninstall script
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = AppContext.BaseDirectory;
                }
                
                var exeDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(exeDir))
                {
                    exeDir = AppContext.BaseDirectory;
                }

                // Look for uninstall script in common locations
                var possiblePaths = new[]
                {
                    Path.Combine(exeDir, "..", "setup", "uninstall.ps1"),
                    Path.Combine(exeDir, "setup", "uninstall.ps1"),
                    Path.Combine(Path.GetDirectoryName(exeDir) ?? "", "setup", "uninstall.ps1"),
                    @"C:\Program Files\Openctrol\setup\uninstall.ps1"
                };

                string? uninstallScript = null;
                foreach (var path in possiblePaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        uninstallScript = fullPath;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(uninstallScript))
                {
                    return Results.Json(new ErrorResponse 
                    { 
                        Error = "script_not_found", 
                        Details = "Uninstall script not found. Please run setup\\uninstall.ps1 manually." 
                    }, statusCode: 400);
                }

                _logger.Warn($"[UI] Uninstall requested via UI. Executing: {uninstallScript}");

                // Launch PowerShell to run uninstall script
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{uninstallScript}\" -RemoveProgramData:$false",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    return Results.Json(new ErrorResponse { Error = "process_start_failed", Details = "Failed to start uninstall process" }, statusCode: 500);
                }

                // Don't wait - return immediately
                return Results.Json(new 
                { 
                    success = true, 
                    message = "Uninstall script invoked. The service will stop and remove itself.",
                    statusCode = 202
                }, statusCode: 202);
            }
            catch (Exception ex)
            {
                _logger.Error("[UI] Error invoking uninstall", ex);
                return Results.Json(new ErrorResponse { Error = "internal_error", Details = SanitizeErrorMessage(ex, "Failed to invoke uninstall") }, statusCode: 500);
            }
        });
    }

    /// <summary>
    /// Gets the current Windows service status.
    /// </summary>
    private ServiceInfoDto GetServiceStatus()
    {
        try
        {
            var serviceName = "OpenctrolAgent";
            using var service = new ServiceController(serviceName);
            
            return new ServiceInfoDto
            {
                ServiceName = serviceName,
                IsServiceInstalled = true,
                ServiceStatus = service.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            // Service not found
            return new ServiceInfoDto
            {
                ServiceName = "OpenctrolAgent",
                IsServiceInstalled = false,
                ServiceStatus = "NotInstalled"
            };
        }
        catch (Exception ex)
        {
            _logger.Debug($"[UI] Error getting service status: {ex.Message}");
            return new ServiceInfoDto
            {
                ServiceName = "OpenctrolAgent",
                IsServiceInstalled = false,
                ServiceStatus = "Unknown"
            };
        }
    }

    /// <summary>
    /// Controls the Windows service (start/stop/restart).
    /// </summary>
    private IResult ControlService(string action)
    {
        try
        {
            var serviceName = "OpenctrolAgent";
            using var service = new ServiceController(serviceName);

            switch (action.ToLower())
            {
                case "stop":
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        _logger.Info("[UI] Service stopped via UI");
                        return Results.Json(new ServiceControlResponse
                        {
                            Success = true,
                            Message = "Service stopped successfully",
                            ServiceStatus = service.Status.ToString()
                        });
                    }
                    return Results.Json(new ServiceControlResponse
                    {
                        Success = true,
                        Message = "Service is already stopped",
                        ServiceStatus = service.Status.ToString()
                    });

                case "start":
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                        _logger.Info("[UI] Service started via UI");
                        return Results.Json(new ServiceControlResponse
                        {
                            Success = true,
                            Message = "Service started successfully",
                            ServiceStatus = service.Status.ToString()
                        });
                    }
                    return Results.Json(new ServiceControlResponse
                    {
                        Success = true,
                        Message = "Service is already running",
                        ServiceStatus = service.Status.ToString()
                    });

                case "restart":
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    _logger.Info("[UI] Service restarted via UI");
                    return Results.Json(new ServiceControlResponse
                    {
                        Success = true,
                        Message = "Service restarted successfully",
                        ServiceStatus = service.Status.ToString()
                    });

                default:
                    return Results.Json(new ServiceControlResponse
                    {
                        Success = false,
                        Message = "Invalid action",
                        Error = "invalid_action",
                        Details = $"Unknown action: {action}"
                    }, statusCode: 400);
            }
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new ServiceControlResponse
            {
                Success = false,
                Message = "Service not found or not accessible",
                Error = "service_not_found",
                Details = ex.Message
            }, statusCode: 404);
        }
        catch (Exception ex)
        {
            _logger.Error($"[UI] Error controlling service ({action})", ex);
            return Results.Json(new ServiceControlResponse
            {
                Success = false,
                Message = "Failed to control service",
                Error = "service_control_failed",
                Details = SanitizeErrorMessage(ex, "Service control operation failed")
            }, statusCode: 500);
        }
    }

    /// <summary>
    /// Returns the HTML content for the UI dashboard.
    /// </summary>
    private static string GetUiHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Openctrol Agent - Control Panel</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            background: #f5f5f5;
            color: #333;
            line-height: 1.6;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
        }
        header {
            background: white;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 20px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        h1 {
            color: #2563eb;
            margin-bottom: 10px;
        }
        .status-pill {
            display: inline-block;
            padding: 4px 12px;
            border-radius: 12px;
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
        }
        .status-running { background: #10b981; color: white; }
        .status-stopped { background: #ef4444; color: white; }
        .status-unknown { background: #6b7280; color: white; }
        .section {
            background: white;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 20px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        .section h2 {
            color: #1f2937;
            margin-bottom: 15px;
            font-size: 18px;
        }
        .info-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 15px;
            margin-bottom: 15px;
        }
        .info-item {
            padding: 10px;
            background: #f9fafb;
            border-radius: 4px;
        }
        .info-label {
            font-size: 12px;
            color: #6b7280;
            margin-bottom: 4px;
        }
        .info-value {
            font-size: 16px;
            font-weight: 600;
            color: #1f2937;
        }
        .btn {
            padding: 10px 20px;
            border: none;
            border-radius: 6px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s;
            margin-right: 10px;
            margin-bottom: 10px;
        }
        .btn-primary { background: #2563eb; color: white; }
        .btn-primary:hover { background: #1d4ed8; }
        .btn-danger { background: #ef4444; color: white; }
        .btn-danger:hover { background: #dc2626; }
        .btn-secondary { background: #6b7280; color: white; }
        .btn-secondary:hover { background: #4b5563; }
        .btn:disabled {
            opacity: 0.5;
            cursor: not-allowed;
        }
        .form-group {
            margin-bottom: 15px;
        }
        label {
            display: block;
            margin-bottom: 5px;
            font-weight: 600;
            color: #374151;
        }
        input[type=""text""], input[type=""number""], textarea {
            width: 100%;
            padding: 8px 12px;
            border: 1px solid #d1d5db;
            border-radius: 6px;
            font-size: 14px;
        }
        input[type=""checkbox""] {
            margin-right: 8px;
        }
        .error-banner {
            background: #fee2e2;
            color: #991b1b;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 15px;
            display: none;
        }
        .success-banner {
            background: #d1fae5;
            color: #065f46;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 15px;
            display: none;
        }
        .hidden { display: none; }
    </style>
</head>
<body>
    <div class=""container"">
        <header>
            <h1>Openctrol Agent</h1>
            <div id=""statusPill"" class=""status-pill status-unknown"">Loading...</div>
            <div style=""margin-top: 10px; color: #6b7280; font-size: 14px;"">
                <span id=""version"">-</span> • Uptime: <span id=""uptime"">-</span>
            </div>
        </header>

        <div class=""error-banner"" id=""errorBanner""></div>
        <div class=""success-banner"" id=""successBanner""></div>

        <div class=""section"">
            <h2>Health & Connection</h2>
            <div class=""info-grid"">
                <div class=""info-item"">
                    <div class=""info-label"">Desktop State</div>
                    <div class=""info-value"" id=""desktopState"">-</div>
                </div>
                <div class=""info-item"">
                    <div class=""info-label"">Status</div>
                    <div class=""info-value"" id=""degradedStatus"">-</div>
                </div>
                <div class=""info-item"">
                    <div class=""info-label"">Active Sessions</div>
                    <div class=""info-value"" id=""activeSessions"">-</div>
                </div>
            </div>
        </div>

        <div class=""section"">
            <h2>Integration & Config</h2>
            <div class=""info-grid"">
                <div class=""info-item"">
                    <div class=""info-label"">Port</div>
                    <div class=""info-value"" id=""configPort"">-</div>
                </div>
                <div class=""info-item"">
                    <div class=""info-label"">HTTPS</div>
                    <div class=""info-value"" id=""configHttps"">-</div>
                </div>
                <div class=""info-item"">
                    <div class=""info-label"">API Key</div>
                    <div class=""info-value"" id=""configApiKey"">-</div>
                </div>
                <div class=""info-item"">
                    <div class=""info-label"">Allowed HA IDs</div>
                    <div class=""info-value"" id=""configHaIds"">-</div>
                </div>
            </div>
            <button class=""btn btn-secondary"" onclick=""toggleConfigForm()"">Edit Config</button>
            <div id=""configForm"" class=""hidden"" style=""margin-top: 20px; padding-top: 20px; border-top: 1px solid #e5e7eb;"">
                <form id=""configFormElement"" onsubmit=""saveConfig(event)"">
                    <div class=""form-group"">
                        <label>Port</label>
                        <input type=""number"" id=""formPort"" min=""1"" max=""65535"" required>
                    </div>
                    <div class=""form-group"">
                        <label>
                            <input type=""checkbox"" id=""formUseHttps"">
                            Use HTTPS
                        </label>
                    </div>
                    <div class=""form-group"" id=""certPathGroup"">
                        <label>Certificate Path</label>
                        <input type=""text"" id=""formCertPath"" placeholder=""C:\\path\\to\\cert.pfx"">
                    </div>
                    <div class=""form-group"">
                        <label>Allowed HA IDs (comma-separated)</label>
                        <textarea id=""formHaIds"" rows=""3"" placeholder=""home-assistant-1, home-assistant-2""></textarea>
                    </div>
                    <div class=""form-group"">
                        <label>New API Key (leave empty to keep current)</label>
                        <input type=""text"" id=""formApiKey"" placeholder=""Leave empty to keep current key"">
                    </div>
                    <button type=""submit"" class=""btn btn-primary"">Save Config</button>
                    <button type=""button"" class=""btn btn-secondary"" onclick=""toggleConfigForm()"">Cancel</button>
                </form>
            </div>
        </div>

        <div class=""section"">
            <h2>Service Controls</h2>
            <button class=""btn btn-secondary"" onclick=""controlService('start')"">Start</button>
            <button class=""btn btn-secondary"" onclick=""controlService('stop')"">Stop</button>
            <button class=""btn btn-secondary"" onclick=""controlService('restart')"">Restart</button>
            <button class=""btn btn-danger"" onclick=""confirmUninstall()"">Uninstall</button>
        </div>
    </div>

    <script src=""/ui/app.js""></script>
</body>
</html>";
    }

    /// <summary>
    /// Returns the JavaScript content for the UI.
    /// </summary>
    private static string GetUiJavaScript()
    {
        return @"
let statusData = null;
let configData = null;

async function loadStatus() {
    try {
        const response = await fetch('/api/v1/ui/status');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        statusData = await response.json();
        updateUI();
    } catch (error) {
        showError('Failed to load status: ' + error.message);
    }
}

async function loadConfig() {
    try {
        const response = await fetch('/api/v1/ui/config');
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        configData = await response.json();
        populateConfigForm();
    } catch (error) {
        showError('Failed to load config: ' + error.message);
    }
}

function updateUI() {
    if (!statusData) return;

    // Status pill
    const statusPill = document.getElementById('statusPill');
    const serviceStatus = statusData.service.service_status;
    statusPill.textContent = serviceStatus;
    statusPill.className = 'status-pill status-' + serviceStatus.toLowerCase().replace(' ', '-');

    // Version and uptime
    document.getElementById('version').textContent = 'v' + statusData.agent.version;
    const uptimeHours = Math.floor(statusData.agent.uptime_seconds / 3600);
    const uptimeMins = Math.floor((statusData.agent.uptime_seconds % 3600) / 60);
    document.getElementById('uptime').textContent = uptimeHours + 'h ' + uptimeMins + 'm';

    // Health
    document.getElementById('desktopState').textContent = statusData.health.desktop_state || 'unknown';
    document.getElementById('degradedStatus').textContent = statusData.health.is_degraded ? 'Degraded' : 'OK';
    document.getElementById('activeSessions').textContent = statusData.health.active_sessions;

    // Config
    document.getElementById('configPort').textContent = statusData.config.port;
    document.getElementById('configHttps').textContent = statusData.config.use_https ? 'Yes' : 'No';
    document.getElementById('configApiKey').textContent = statusData.config.api_key_configured ? 'Configured' : 'Not set';
    document.getElementById('configHaIds').textContent = statusData.config.allowed_ha_ids.length > 0 
        ? statusData.config.allowed_ha_ids.join(', ') 
        : 'None (deny all)';
}

function populateConfigForm() {
    if (!configData) return;
    document.getElementById('formPort').value = configData.port;
    document.getElementById('formUseHttps').checked = configData.use_https;
    document.getElementById('formCertPath').value = configData.cert_path || '';
    document.getElementById('formHaIds').value = configData.allowed_ha_ids.join(', ');
    document.getElementById('formApiKey').value = '';
    updateCertPathVisibility();
}

function updateCertPathVisibility() {
    const useHttps = document.getElementById('formUseHttps').checked;
    document.getElementById('certPathGroup').style.display = useHttps ? 'block' : 'none';
}

document.getElementById('formUseHttps').addEventListener('change', updateCertPathVisibility);

function toggleConfigForm() {
    const form = document.getElementById('configForm');
    form.classList.toggle('hidden');
    if (!form.classList.contains('hidden')) {
        loadConfig();
    }
}

async function saveConfig(event) {
    event.preventDefault();
    hideMessages();

    const formData = {
        port: parseInt(document.getElementById('formPort').value),
        use_https: document.getElementById('formUseHttps').checked,
        cert_path: document.getElementById('formCertPath').value,
        allowed_ha_ids: document.getElementById('formHaIds').value.split(',').map(s => s.trim()).filter(s => s),
        api_key: document.getElementById('formApiKey').value || null
    };

    try {
        const response = await fetch('/api/v1/ui/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(formData)
        });

        const result = await response.json();
        if (!response.ok) {
            throw new Error(result.details || result.error || 'Failed to save config');
        }

        showSuccess(result.message || 'Config saved. Please restart the service to apply changes.');
        toggleConfigForm();
        setTimeout(loadStatus, 1000);
    } catch (error) {
        showError('Failed to save config: ' + error.message);
    }
}

async function controlService(action) {
    hideMessages();
    try {
        const response = await fetch(`/api/v1/ui/service/${action}`, {
            method: 'POST'
        });

        const result = await response.json();
        if (!response.ok) {
            throw new Error(result.details || result.error || `Failed to ${action} service`);
        }

        showSuccess(result.message || `Service ${action}ed successfully`);
        setTimeout(loadStatus, 1000);
    } catch (error) {
        showError('Failed to control service: ' + error.message);
    }
}

function confirmUninstall() {
    if (!confirm('Are you sure? This will uninstall Openctrol Agent from this machine.')) {
        return;
    }
    
    if (!confirm('This action cannot be undone. Continue?')) {
        return;
    }

    hideMessages();
    fetch('/api/v1/ui/service/uninstall', {
        method: 'POST'
    })
    .then(response => response.json())
    .then(result => {
        if (result.success) {
            showSuccess(result.message || 'Uninstall initiated. The service will stop and remove itself.');
        } else {
            showError(result.details || result.error || 'Failed to initiate uninstall');
        }
    })
    .catch(error => {
        showError('Failed to initiate uninstall: ' + error.message);
    });
}

function showError(message) {
    const banner = document.getElementById('errorBanner');
    banner.textContent = message;
    banner.style.display = 'block';
    setTimeout(hideMessages, 5000);
}

function showSuccess(message) {
    const banner = document.getElementById('successBanner');
    banner.textContent = message;
    banner.style.display = 'block';
    setTimeout(hideMessages, 5000);
}

function hideMessages() {
    document.getElementById('errorBanner').style.display = 'none';
    document.getElementById('successBanner').style.display = 'none';
}

// Load data on page load
loadStatus();
setInterval(loadStatus, 5000); // Refresh every 5 seconds
";
    }
}

