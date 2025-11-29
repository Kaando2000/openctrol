using Openctrol.Agent.Config;
using Openctrol.Agent.Logging;
using Openctrol.Agent.Web;
using Xunit;

namespace Openctrol.Agent.Tests;

public class SessionBrokerTests
{
    [Fact]
    public void StartDesktopSession_WithinLimit_CreatesSession()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.MaxSessions = 2;
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);

        // Act
        var session = sessionBroker.StartDesktopSession("test-ha-id", TimeSpan.FromMinutes(15));

        // Assert
        Assert.NotNull(session);
        Assert.NotEmpty(session.SessionId);
        Assert.Equal("test-ha-id", session.HaId);
        Assert.True(session.IsActive);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void StartDesktopSession_ExceedsMaxSessions_ThrowsException()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.MaxSessions = 1;
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);
        sessionBroker.StartDesktopSession("test-ha-id-1", TimeSpan.FromMinutes(15));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            sessionBroker.StartDesktopSession("test-ha-id-2", TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void TryGetSession_ValidSessionId_ReturnsTrue()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);
        var session = sessionBroker.StartDesktopSession("test-ha-id", TimeSpan.FromMinutes(15));

        // Act
        var exists = sessionBroker.TryGetSession(session.SessionId, out var retrievedSession);

        // Assert
        Assert.True(exists);
        Assert.NotNull(retrievedSession);
        Assert.Equal(session.SessionId, retrievedSession.SessionId);
    }

    [Fact]
    public void TryGetSession_InvalidSessionId_ReturnsFalse()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);

        // Act
        var exists = sessionBroker.TryGetSession("invalid-session-id", out var retrievedSession);

        // Assert
        Assert.False(exists);
        Assert.Null(retrievedSession);
    }

    [Fact]
    public void EndSession_ValidSessionId_RemovesSession()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);
        var session = sessionBroker.StartDesktopSession("test-ha-id", TimeSpan.FromMinutes(15));

        // Act
        sessionBroker.EndSession(session.SessionId);

        // Assert
        Assert.False(sessionBroker.TryGetSession(session.SessionId, out _));
    }

    [Fact]
    public void GetActiveSessions_ReturnsOnlyActiveSessions()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.MaxSessions = 3;
        var logger = new CompositeLogger(new NullLogger());
        var sessionBroker = new SessionBroker(configManager, logger);
        var session1 = sessionBroker.StartDesktopSession("test-ha-id-1", TimeSpan.FromMinutes(15));
        var session2 = sessionBroker.StartDesktopSession("test-ha-id-2", TimeSpan.FromMinutes(15));

        // Act
        var activeSessions = sessionBroker.GetActiveSessions();

        // Assert
        Assert.Equal(2, activeSessions.Count);
        Assert.Contains(activeSessions, s => s.SessionId == session1.SessionId);
        Assert.Contains(activeSessions, s => s.SessionId == session2.SessionId);
    }

    // Helper class for testing
    private class NullLogger : ILogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void Debug(string message) { }
    }
}

