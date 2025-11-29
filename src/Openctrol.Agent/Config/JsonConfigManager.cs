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
                    return config;
                }
            }
            catch
            {
                // Fall through to create default
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
            AllowedHaIds = new List<string>()
        };

        try
        {
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
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

