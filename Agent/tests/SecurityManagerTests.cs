using System.Text.Json;
using Openctrol.Agent.Config;
using Openctrol.Agent.Logging;
using Openctrol.Agent.Security;
using Xunit;

namespace Openctrol.Agent.Tests;

public class SecurityManagerTests
{
    [Fact]
    public void IssueDesktopSessionToken_ValidHaId_ReturnsToken()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        // Add test HA ID to allowlist
        config.AllowedHaIds = new List<string> { "test-ha-id" };
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);

        // Act
        var token = securityManager.IssueDesktopSessionToken("test-ha-id", TimeSpan.FromMinutes(15));

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token.Token);
        Assert.Equal("test-ha-id", token.HaId);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TryValidateDesktopSessionToken_ValidToken_ReturnsTrue()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        // Add test HA ID to allowlist
        config.AllowedHaIds = new List<string> { "test-ha-id" };
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);
        var issuedToken = securityManager.IssueDesktopSessionToken("test-ha-id", TimeSpan.FromMinutes(15));

        // Act
        var isValid = securityManager.TryValidateDesktopSessionToken(issuedToken.Token, out var validatedToken);

        // Assert
        Assert.True(isValid);
        Assert.NotNull(validatedToken);
        Assert.Equal(issuedToken.Token, validatedToken.Token);
        Assert.Equal("test-ha-id", validatedToken.HaId);
    }

    [Fact]
    public void TryValidateDesktopSessionToken_InvalidToken_ReturnsFalse()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);

        // Act
        var isValid = securityManager.TryValidateDesktopSessionToken("invalid-token", out var validatedToken);

        // Assert
        Assert.False(isValid);
        Assert.Null(validatedToken);
    }

    [Fact]
    public void TryValidateDesktopSessionToken_ExpiredToken_ReturnsFalse()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        // Add test HA ID to allowlist
        config.AllowedHaIds = new List<string> { "test-ha-id" };
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);
        var issuedToken = securityManager.IssueDesktopSessionToken("test-ha-id", TimeSpan.FromMilliseconds(1));

        // Wait for token to expire
        Thread.Sleep(100);

        // Act
        var isValid = securityManager.TryValidateDesktopSessionToken(issuedToken.Token, out var validatedToken);

        // Assert
        Assert.False(isValid);
        Assert.Null(validatedToken);
    }

    [Fact]
    public void IsHaAllowed_NoAllowlist_ReturnsFalse()
    {
        // Arrange - Empty allowlist means deny-all (secure by default)
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.AllowedHaIds = new List<string>(); // Explicitly empty
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);

        // Act
        var isAllowed = securityManager.IsHaAllowed("any-ha-id");

        // Assert
        Assert.False(isAllowed); // Empty allowlist = deny-all
    }

    [Fact]
    public void IsHaAllowed_NullAllowedHaIds_ReturnsFalse()
    {
        // Arrange - Simulate JSON deserialization that sets AllowedHaIds to null
        var jsonWithoutAllowedHaIds = """{"AgentId":"test-id","HttpPort":44325,"MaxSessions":1,"TargetFps":30}""";
        var config = JsonSerializer.Deserialize<AgentConfig>(jsonWithoutAllowedHaIds);
        Assert.NotNull(config);
        
        // System.Text.Json may set AllowedHaIds to null if not in JSON
        // This test verifies the null check in IsHaAllowed handles this case (deny-all)
        if (config!.AllowedHaIds == null)
        {
            // Create a mock config manager that returns config with null AllowedHaIds
            var mockConfigManager = new MockConfigManager(config);
            var logger = new CompositeLogger(new NullLogger());
            var securityManager = new SecurityManager(mockConfigManager, logger);

            // Act
            var isAllowed = securityManager.IsHaAllowed("any-ha-id");

            // Assert
            Assert.False(isAllowed); // Null/empty allowlist = deny-all (secure by default)
        }
    }

    [Fact]
    public void IsHaAllowed_WithAllowlist_ReturnsTrueForAllowed()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.AllowedHaIds = new List<string> { "allowed-id-1", "allowed-id-2" };
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);

        // Act
        var isAllowed = securityManager.IsHaAllowed("allowed-id-1");

        // Assert
        Assert.True(isAllowed);
    }

    [Fact]
    public void IsHaAllowed_WithAllowlist_ReturnsFalseForNotAllowed()
    {
        // Arrange
        var configManager = new JsonConfigManager();
        var config = configManager.GetConfig();
        config.AllowedHaIds = new List<string> { "allowed-id-1", "allowed-id-2" };
        var logger = new CompositeLogger(new NullLogger());
        var securityManager = new SecurityManager(configManager, logger);

        // Act
        var isAllowed = securityManager.IsHaAllowed("not-allowed-id");

        // Assert
        Assert.False(isAllowed);
    }
    
    // Helper class for testing
    private class MockConfigManager : IConfigManager
    {
        private readonly AgentConfig _config;
        
        public MockConfigManager(AgentConfig config)
        {
            _config = config;
        }
        
        public AgentConfig GetConfig() => _config;
        public void Reload() { }
        public void ValidateConfig() { }
        public void SaveConfig(AgentConfig config) { }
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

