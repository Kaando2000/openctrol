using System.Security.Cryptography;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Security;

public sealed class SecurityManager : ISecurityManager
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly Dictionary<string, SessionToken> _tokens = new();
    private readonly Dictionary<string, (int count, DateTime windowStart)> _validationFailures = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly object _lock = new();
    private const int MaxFailuresPerWindow = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    public SecurityManager(IConfigManager configManager, ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;

        // Clean up expired tokens periodically - store timer to prevent GC
        _cleanupTimer = new System.Threading.Timer(CleanupExpiredTokens, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsHaAllowed(string haId)
    {
        var config = _configManager.GetConfig();
        // Handle null AllowedHaIds (defensive check in case deserialization didn't initialize it)
        if (config.AllowedHaIds == null || config.AllowedHaIds.Count == 0)
        {
            // If no allowlist is configured, allow all (for development)
            return true;
        }

        return config.AllowedHaIds.Contains(haId);
    }

    public SessionToken IssueDesktopSessionToken(string haId, TimeSpan ttl)
    {
        if (!IsHaAllowed(haId))
        {
            throw new UnauthorizedAccessException($"HA ID {haId} is not allowed");
        }

        var token = GenerateToken();
        var sessionToken = new SessionToken
        {
            Token = token,
            HaId = haId,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };

        lock (_lock)
        {
            _tokens[token] = sessionToken;
        }

        _logger.Info($"Issued desktop session token for HA ID: {haId}, expires at: {sessionToken.ExpiresAt}");
        return sessionToken;
    }

    public bool TryValidateDesktopSessionToken(string token, out SessionToken validated)
    {
        lock (_lock)
        {
            // Check rate limiting
            var clientId = GetClientIdFromToken(token); // Use token prefix as client identifier
            if (IsRateLimited(clientId))
            {
                _logger.Warn($"Rate limit exceeded for token validation from {clientId}");
                validated = null!;
                return false;
            }

            if (_tokens.TryGetValue(token, out var sessionToken))
            {
                if (sessionToken.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    validated = sessionToken;
                    return true;
                }
                else
                {
                    // Token expired, remove it
                    _tokens.Remove(token);
                }
            }

            // Record validation failure
            RecordValidationFailure(clientId);
        }

        validated = null!;
        return false;
    }

    private bool IsRateLimited(string clientId)
    {
        if (!_validationFailures.TryGetValue(clientId, out var failureInfo))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (now - failureInfo.windowStart > FailureWindow)
        {
            // Window expired, reset
            _validationFailures.Remove(clientId);
            return false;
        }

        return failureInfo.count >= MaxFailuresPerWindow;
    }

    private void RecordValidationFailure(string clientId)
    {
        var now = DateTime.UtcNow;
        if (_validationFailures.TryGetValue(clientId, out var failureInfo))
        {
            if (now - failureInfo.windowStart > FailureWindow)
            {
                // Reset window
                _validationFailures[clientId] = (1, now);
            }
            else
            {
                // Increment count
                _validationFailures[clientId] = (failureInfo.count + 1, failureInfo.windowStart);
            }
        }
        else
        {
            _validationFailures[clientId] = (1, now);
        }
    }

    private static string GetClientIdFromToken(string token)
    {
        // Use first 8 characters of token as client identifier
        return token.Length >= 8 ? token.Substring(0, 8) : token;
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private void CleanupExpiredTokens(object? state)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var expiredTokens = _tokens
                .Where(kvp => kvp.Value.ExpiresAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in expiredTokens)
            {
                _tokens.Remove(token);
            }

            if (expiredTokens.Count > 0)
            {
                _logger.Info($"Cleaned up {expiredTokens.Count} expired tokens");
            }
        }
    }
}

