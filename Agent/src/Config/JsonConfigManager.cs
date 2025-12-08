using System.Security.Principal;
using System.Text.Json;

namespace Openctrol.Agent.Config;

public sealed class JsonConfigManager : IConfigManager
{
    private readonly string _configPath;
    private AgentConfig _config;
    private readonly object _lock = new();

    public JsonConfigManager()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var configDir = Path.Combine(programData, "Openctrol");
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "config.json");
        _config = LoadOrCreateDefault();
    }

    public AgentConfig GetConfig()
    {
        lock (_lock)
        {
            return _config;
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _config = LoadOrCreateDefault();
        }
    }

    public void ValidateConfig()
    {
        lock (_lock)
        {
            var config = _config;
            
            // Validate HttpPort
            if (config.HttpPort <= 0)
            {
                throw new InvalidOperationException($"Invalid HttpPort: {config.HttpPort}. Must be greater than 0");
            }
            
            // Ports < 1024 require administrator privileges on Windows
            if (config.HttpPort < 1024)
            {
                if (!IsRunningAsAdministrator())
                {
                    throw new InvalidOperationException($"Invalid HttpPort: {config.HttpPort}. Ports below 1024 require administrator privileges. Either use a port >= 1024 or run the service as administrator.");
                }
            }

            // Validate MaxSessions
            if (config.MaxSessions < 1)
            {
                throw new InvalidOperationException($"Invalid MaxSessions: {config.MaxSessions}. Must be >= 1");
            }

            // Validate TargetFps
            if (config.TargetFps < 1 || config.TargetFps > 120)
            {
                throw new InvalidOperationException($"Invalid TargetFps: {config.TargetFps}. Must be between 1 and 120");
            }

            // Validate AgentId
            if (string.IsNullOrEmpty(config.AgentId))
            {
                throw new InvalidOperationException("AgentId is required and cannot be empty");
            }

            // Validate certificate if configured
            if (!string.IsNullOrEmpty(config.CertPath))
            {
                if (!File.Exists(config.CertPath))
                {
                    throw new InvalidOperationException($"Certificate file not found: {config.CertPath}");
                }
            }

            // Security warning: API key validation
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                // Log warning but don't fail startup - allows development mode
                // In production, ApiKey should always be configured
                // This warning will be logged at startup by AgentHost
            }
        }
    }

    public void SaveConfig(AgentConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        lock (_lock)
        {
            // Validate the config before saving
            var originalConfig = _config;
            _config = config; // Temporarily set for validation
            try
            {
                ValidateConfig();
            }
            catch
            {
                _config = originalConfig; // Restore on validation failure
                throw;
            }

            // Ensure AllowedHaIds is never null
            if (config.AllowedHaIds == null)
            {
                config.AllowedHaIds = new List<string>();
            }

            // Ensure AgentId is set
            if (string.IsNullOrEmpty(config.AgentId))
            {
                config.AgentId = Guid.NewGuid().ToString();
            }

            // Serialize and write to file
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);

            // Set restrictive file permissions
            try
            {
                var fileInfo = new FileInfo(_configPath);
                var fileSecurity = fileInfo.GetAccessControl();
                
                // Remove inherited permissions
                fileSecurity.SetAccessRuleProtection(true, false);
                
                // Remove all existing access rules
                var existingRules = fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
                foreach (System.Security.AccessControl.AuthorizationRule rule in existingRules)
                {
                    var accessRule = rule as System.Security.AccessControl.FileSystemAccessRule;
                    if (accessRule != null)
                    {
                        fileSecurity.RemoveAccessRule(accessRule);
                    }
                }
                
                // Grant full control to Administrators and SYSTEM
                var adminSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
                
                fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    adminSid, 
                    System.Security.AccessControl.FileSystemRights.FullControl, 
                    System.Security.AccessControl.AccessControlType.Allow));
                
                fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    systemSid, 
                    System.Security.AccessControl.FileSystemRights.FullControl, 
                    System.Security.AccessControl.AccessControlType.Allow));
                
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch
            {
                // Log but don't fail - permissions may not be settable in all environments
            }

            // Update in-memory config
            _config = config;
        }
    }

    private AgentConfig LoadOrCreateDefault()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AgentConfig>(json);
                if (config != null)
                {
                    // Ensure AllowedHaIds is never null (System.Text.Json may set it to null if missing from JSON)
                    if (config.AllowedHaIds == null)
                    {
                        config.AllowedHaIds = new List<string>();
                    }
                    
                    // Ensure AgentId is non-empty (generate if missing)
                    if (string.IsNullOrEmpty(config.AgentId))
                    {
                        config.AgentId = Guid.NewGuid().ToString();
                        // Persist the new AgentId back to config file
                        try
                        {
                            var updatedJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(_configPath, updatedJson);
                        }
                        catch
                        {
                            // Continue even if write fails - at least we have a valid AgentId in memory
                        }
                    }
                    
                    return config;
                }
            }
            catch (JsonException ex)
            {
                // Log JSON parsing errors to help diagnose config file corruption
                // Note: We can't use _logger here as this is called during construction
                // Fall through to create default config
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent",
                        $"Failed to parse config file {_configPath}: {ex.Message}. Creating default config.",
                        System.Diagnostics.EventLogEntryType.Warning);
                }
                catch
                {
                    // If EventLog write fails, continue silently
                }
            }
            catch (Exception ex)
            {
                // Log other errors (file access, etc.)
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent",
                        $"Error loading config file {_configPath}: {ex.Message}. Creating default config.",
                        System.Diagnostics.EventLogEntryType.Warning);
                }
                catch
                {
                    // If EventLog write fails, continue silently
                }
            }
        }

        // Create default config
        var defaultConfig = new AgentConfig
        {
            AgentId = Guid.NewGuid().ToString(),
            HttpPort = 44325,
            MaxSessions = 1,
            CertPath = "",
            CertPasswordEncrypted = "",
            TargetFps = 30,
            AllowedHaIds = new List<string>(),
            ApiKey = "" // Empty = no authentication required (for development)
        };

        try
        {
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            
            // Set restrictive file permissions: owner (Administrators/LocalSystem) only
            // This prevents unauthorized users from reading the config file (which may contain encrypted passwords)
            try
            {
                var fileInfo = new FileInfo(_configPath);
                var fileSecurity = fileInfo.GetAccessControl();
                
                // Remove inherited permissions
                fileSecurity.SetAccessRuleProtection(true, false);
                
                // Grant full control to Administrators and SYSTEM
                var adminSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
                
                fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    adminSid, 
                    System.Security.AccessControl.FileSystemRights.FullControl, 
                    System.Security.AccessControl.AccessControlType.Allow));
                
                fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    systemSid, 
                    System.Security.AccessControl.FileSystemRights.FullControl, 
                    System.Security.AccessControl.AccessControlType.Allow));
                
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch
            {
                // Log but don't fail - permissions may not be settable in all environments
                // This is a security hardening measure, not a hard requirement
            }
        }
        catch
        {
            // Continue with in-memory default if file write fails
        }

        return defaultConfig;
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we can't determine, assume not admin for safety
            return false;
        }
    }
}

