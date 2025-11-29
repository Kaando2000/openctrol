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
}

