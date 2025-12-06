using System.Security.Cryptography;
using Openctrol.Agent.Config;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Security;

public sealed class SecurityManager : ISecurityManager, IDisposable
{
    private readonly IConfigManager _configManager;
    private readonly ILogger _logger;
    private readonly Dictionary<string, SessionToken> _tokens = new();
    private readonly Dictionary<string, (int count, DateTime windowStart)> _validationFailures = new();
    private readonly HashSet<string> _revokedTokens = new(); // Track revoked tokens
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

    // NOTE: HA installation ID allowlist is currently disabled by design.
    // Authentication is enforced via API key + LAN/localhost checks only.
    public bool IsHaAllowed(string haId)
    {
        var config = _configManager.GetConfig();
        
        // Log the received HA ID for informational purposes, but do not block requests
        if (config.AllowedHaIds != null && config.AllowedHaIds.Count > 0)
        {
            _logger.Info($"[Security] Ignoring HA ID allowlist (feature disabled). Received ID: {haId}");
        }
        else
        {
            _logger.Debug($"[Security] HA ID allowlist check disabled. Received ID: {haId}");
        }
        
        // Always allow - HA ID allowlist check is disabled
        return true;
    }

    public SessionToken IssueDesktopSessionToken(string haId, TimeSpan ttl)
    {
        // NOTE: HA ID allowlist check is disabled - IsHaAllowed() always returns true
        // This call is kept for logging purposes only
        IsHaAllowed(haId);

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

        _logger.Info($"[Security] Issued desktop session token for HA ID: {haId}, expires at: {sessionToken.ExpiresAt}");
        return sessionToken;
    }

    /// <summary>
    /// Validates a desktop session token.
    /// 
    /// Token revocation semantics:
    /// - If the token has been revoked via RevokeToken(), validation immediately fails.
    /// - Revoked tokens are checked first, before any other validation.
    /// - When a token is revoked, it is removed from active tokens and added to the revocation list.
    /// - Revoked tokens will fail validation even if they haven't expired yet.
    /// - This ensures that compromised or intentionally invalidated tokens cannot be used.
    /// 
    /// Token expiration:
    /// - Tokens are also checked for expiration (ExpiresAt > now).
    /// - Expired tokens are automatically removed from active tokens.
    /// 
    /// Rate limiting:
    /// - Failed validation attempts are rate-limited to prevent brute force attacks.
    /// </summary>
    public bool TryValidateDesktopSessionToken(string token, out SessionToken validated)
    {
        lock (_lock)
        {
            // TOKEN REVOCATION CHECK: Reject revoked tokens immediately
            // Revoked tokens are invalid regardless of expiration status
            if (_revokedTokens.Contains(token))
            {
                _logger.Debug($"[Security] Token validation failed - token has been revoked");
                validated = null!;
                return false;
            }

            // Check rate limiting using token hash (more reliable than prefix)
            // This prevents collisions between different tokens with similar prefixes
            var clientId = GetTokenHash(token);
            if (IsRateLimited(clientId))
            {
                _logger.Warn($"[Security] Rate limit exceeded for token validation");
                validated = null!;
                return false;
            }

            // Check if token exists and is not expired
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

    /// <summary>
    /// Revokes a session token, making it permanently invalid.
    /// 
    /// Revocation semantics:
    /// - The token is immediately removed from active tokens.
    /// - The token is added to the revocation list.
    /// - Subsequent calls to TryValidateDesktopSessionToken() will fail for this token,
    ///   even if the token hasn't expired yet.
    /// - Revoked tokens remain in the revocation list until they expire and are cleaned up,
    ///   or until the revocation list size limit is reached.
    /// 
    /// Use cases:
    /// - Revoke tokens when a session is ended via POST /api/v1/sessions/desktop/{id}/end
    /// - Revoke tokens when security breach is suspected
    /// - Revoke tokens when user logs out or session is terminated
    /// 
    /// Note: This does NOT close WebSocket connections. To close WebSocket connections,
    /// use SessionBroker.EndSession() which will cancel the WebSocket handler's cancellation token.
    /// </summary>
    public void RevokeToken(string token)
    {
        lock (_lock)
        {
            // Get HA ID before removing (for logging)
            string? haId = null;
            if (_tokens.TryGetValue(token, out var sessionToken))
            {
                haId = sessionToken.HaId;
            }
            
            // Remove from active tokens - token is no longer valid
            _tokens.Remove(token);
            
            // Add to revocation list - prevents token from being validated even if re-added
            _revokedTokens.Add(token);
            
            _logger.Info($"[Security] Token revoked (HA ID: {haId ?? "unknown"})");
        }
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

    private static string GetTokenHash(string token)
    {
        // Use a hash of the token as client identifier to avoid collisions
        // This is more reliable than using token prefix
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes).Substring(0, 16); // Use first 16 chars of hash
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
                // Also remove from revocation list if present (cleanup)
                _revokedTokens.Remove(token);
            }

            // Clean up old revocation entries (keep for 24 hours after token expiry)
            // This prevents revocation list from growing indefinitely
            // Note: We don't track revocation time, so we'll just limit the size
            // In practice, revoked tokens are removed when expired tokens are cleaned up
            if (_revokedTokens.Count > 1000)
            {
                // If revocation list gets too large, clear it (tokens should have expired by then)
                _revokedTokens.Clear();
                _logger.Debug("[Security] Cleared revocation list (size limit reached)");
            }

            if (expiredTokens.Count > 0)
            {
                _logger.Info($"[Security] Cleaned up {expiredTokens.Count} expired tokens");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

