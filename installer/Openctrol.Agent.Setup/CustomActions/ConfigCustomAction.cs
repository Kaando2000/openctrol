using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Deployment.WindowsInstaller;

namespace Openctrol.Agent.Setup.CustomActions
{
    public class ConfigCustomAction
    {
        /// <summary>
        /// Creates or updates the config.json file in ProgramData\Openctrol.
        /// This custom action runs with elevated privileges during installation.
        /// </summary>
        [CustomAction]
        public static ActionResult CreateConfigFile(Session session)
        {
            try
            {
                session.Log("=== Begin ConfigCustomAction.CreateConfigFile ===");

                // Validate session
                if (session == null)
                {
                    Console.Error.WriteLine("ERROR: Session is null");
                    return ActionResult.Failure;
                }

                // Parse custom action data
                session.Log("Reading custom action data from session...");
                var data = session.CustomActionData;
                if (data == null)
                {
                    session.Log("ERROR: CustomActionData is null");
                    return ActionResult.Failure;
                }

                session.Log($"CustomActionData keys: {string.Join(", ", data.Keys)}");

                var installFolder = data["INSTALLFOLDER"] ?? "";
                var portStr = data["PORT"] ?? "";
                var useHttpsStr = data["USEHTTPS"] ?? "";
                var certPath = data["CERTPATH"] ?? "";
                var certPassword = data["CERTPASSWORD"] ?? "";
                var apiKey = data["APIKEY"] ?? "";
                var agentId = data["AGENTID"] ?? "";

                session.Log($"Install folder: {(string.IsNullOrEmpty(installFolder) ? "(empty)" : installFolder)}");
                session.Log($"Port: {(string.IsNullOrEmpty(portStr) ? "(empty)" : portStr)}");
                session.Log($"Use HTTPS: {useHttpsStr}");
                session.Log($"Cert path: {(string.IsNullOrEmpty(certPath) ? "(not provided)" : certPath)}");
                session.Log($"API Key provided: {!string.IsNullOrEmpty(apiKey)}");
                session.Log($"Agent ID provided: {!string.IsNullOrEmpty(agentId)}");

                // Validate and parse port
                if (string.IsNullOrEmpty(portStr))
                {
                    session.Log("ERROR: PORT property is missing or empty");
                    return ActionResult.Failure;
                }

                if (!int.TryParse(portStr, out int port) || port <= 0 || port > 65535)
                {
                    session.Log($"ERROR: Invalid PORT value: '{portStr}'. Must be a number between 1 and 65535");
                    return ActionResult.Failure;
                }

                session.Log($"Parsed port: {port}");

                // Parse UseHttps
                var useHttps = useHttpsStr == "1";
                session.Log($"Use HTTPS: {useHttps}");

                // Validate HTTPS configuration if enabled
                if (useHttps)
                {
                    if (string.IsNullOrEmpty(certPath))
                    {
                        session.Log("ERROR: HTTPS is enabled but CERTPATH is not provided");
                        return ActionResult.Failure;
                    }

                    if (!File.Exists(certPath))
                    {
                        session.Log($"ERROR: Certificate file not found: {certPath}");
                        return ActionResult.Failure;
                    }

                    if (string.IsNullOrEmpty(certPassword))
                    {
                        session.Log("WARNING: HTTPS is enabled but certificate password is not provided");
                    }
                }

                // Generate API key if not provided
                if (string.IsNullOrEmpty(apiKey))
                {
                    session.Log("API key not provided, generating new one...");
                    apiKey = GenerateRandomApiKey();
                    session.Log("API key generated successfully");
                }

                // Generate Agent ID if not provided
                if (string.IsNullOrEmpty(agentId))
                {
                    session.Log("Agent ID not provided, generating new one...");
                    agentId = Guid.NewGuid().ToString();
                    session.Log($"Agent ID generated: {agentId}");
                }

                // Encrypt certificate password if provided
                var certPasswordEncrypted = "";
                if (!string.IsNullOrEmpty(certPassword))
                {
                    try
                    {
                        session.Log("Encrypting certificate password with DPAPI...");
                        certPasswordEncrypted = EncryptWithDPAPI(certPassword);
                        session.Log("Certificate password encrypted successfully");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"ERROR: Failed to encrypt certificate password: {ex.GetType().Name}: {ex.Message}");
                        session.Log($"Stack trace: {ex.StackTrace}");
                        return ActionResult.Failure;
                    }
                }

                // Resolve ProgramData path
                session.Log("Resolving ProgramData path...");
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (string.IsNullOrEmpty(programData))
                {
                    session.Log("ERROR: Could not resolve ProgramData folder path");
                    return ActionResult.Failure;
                }

                var configDir = Path.Combine(programData, "Openctrol");
                var configPath = Path.Combine(configDir, "config.json");

                session.Log($"Config directory: {configDir}");
                session.Log($"Config file path: {configPath}");

                // Check if config already exists
                if (File.Exists(configPath))
                {
                    session.Log($"Config file already exists at {configPath}, skipping creation");
                    
                    // Read actual values from existing config file
                    try
                    {
                        session.Log("Reading existing config file...");
                        var existingConfigJson = File.ReadAllText(configPath, Encoding.UTF8);
                        var existingConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingConfigJson);
                        
                        // Extract actual ApiKey and AgentId from existing config
                        var actualApiKey = existingConfig?.TryGetValue("ApiKey", out var apiKeyElement) == true 
                            ? apiKeyElement.GetString() ?? "" 
                            : "";
                        var actualAgentId = existingConfig?.TryGetValue("AgentId", out var agentIdElement) == true 
                            ? agentIdElement.GetString() ?? "" 
                            : "";
                        
                        // Store actual values from existing config for summary dialog
                        session["CONFIG_APIKEY_GENERATED"] = actualApiKey;
                        session["CONFIG_AGENTID_GENERATED"] = actualAgentId;
                        
                        session.Log($"Using existing config values - Agent ID: {(!string.IsNullOrEmpty(actualAgentId) ? "present" : "missing")}, API Key: {(!string.IsNullOrEmpty(actualApiKey) ? "present" : "missing")}");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"WARNING: Could not read existing config file: {ex.GetType().Name}: {ex.Message}");
                        session.Log($"Stack trace: {ex.StackTrace}");
                        // Fallback to generated values if we can't read the file
                        session["CONFIG_APIKEY_GENERATED"] = apiKey;
                        session["CONFIG_AGENTID_GENERATED"] = agentId;
                    }
                    
                    session.Log("=== ConfigCustomAction.CreateConfigFile completed (existing file) ===");
                    return ActionResult.Success;
                }

                // Create directory if it doesn't exist
                session.Log($"Creating config directory: {configDir}");
                try
                {
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                        session.Log("Config directory created successfully");
                    }
                    else
                    {
                        session.Log("Config directory already exists");
                    }

                    // Verify directory was created
                    if (!Directory.Exists(configDir))
                    {
                        session.Log($"ERROR: Failed to create config directory: {configDir}");
                        return ActionResult.Failure;
                    }
                }
                catch (Exception ex)
                {
                    session.Log($"ERROR: Failed to create config directory: {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    return ActionResult.Failure;
                }

                // Create config object matching AgentConfig structure
                session.Log("Creating config object...");
                var config = new Dictionary<string, object>
                {
                    { "AgentId", agentId },
                    { "HttpPort", port },
                    { "MaxSessions", 1 },
                    { "CertPath", certPath },
                    { "CertPasswordEncrypted", certPasswordEncrypted },
                    { "TargetFps", 30 },
                    { "AllowedHaIds", new List<string>() },
                    { "ApiKey", apiKey }
                };

                // Serialize to JSON
                session.Log("Serializing config to JSON...");
                string json;
                try
                {
                    json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    session.Log("Config serialized successfully");
                }
                catch (Exception ex)
                {
                    session.Log($"ERROR: Failed to serialize config to JSON: {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    return ActionResult.Failure;
                }

                // Write config file
                session.Log($"Writing config file to: {configPath}");
                try
                {
                    File.WriteAllText(configPath, json, Encoding.UTF8);
                    session.Log("Config file written successfully");

                    // Verify file was created
                    if (!File.Exists(configPath))
                    {
                        session.Log($"ERROR: Config file was not created at: {configPath}");
                        return ActionResult.Failure;
                    }

                    var fileInfo = new FileInfo(configPath);
                    session.Log($"Config file size: {fileInfo.Length} bytes");
                }
                catch (UnauthorizedAccessException ex)
                {
                    session.Log($"ERROR: UnauthorizedAccessException writing config file: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    return ActionResult.Failure;
                }
                catch (DirectoryNotFoundException ex)
                {
                    session.Log($"ERROR: DirectoryNotFoundException writing config file: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    return ActionResult.Failure;
                }
                catch (Exception ex)
                {
                    session.Log($"ERROR: Exception writing config file: {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    return ActionResult.Failure;
                }

                // Set restrictive permissions (Administrators and SYSTEM only)
                // This is important for security as the config contains API keys and encrypted passwords
                session.Log("Setting config file permissions...");
                try
                {
                    var fileInfo = new FileInfo(configPath);
                    var fileSecurity = fileInfo.GetAccessControl();
                    
                    // Remove inherited permissions and set explicit permissions
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
                    
                    // Add Administrators group with FullControl
                    var adminSid = new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                    fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        adminSid, 
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.InheritanceFlags.None,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Allow));
                    
                    // Add SYSTEM account with FullControl
                    var systemSid = new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
                    fileSecurity.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        systemSid, 
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.InheritanceFlags.None,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Allow));
                    
                    fileInfo.SetAccessControl(fileSecurity);
                    session.Log("Config file permissions set successfully (Administrators and SYSTEM only)");
                }
                catch (UnauthorizedAccessException ex)
                {
                    session.Log($"WARNING: Insufficient privileges to set config file permissions: {ex.Message}");
                    session.Log("Config file created but with default permissions. Consider setting permissions manually for security.");
                    // Continue - permissions are a hardening measure, not critical for installation
                }
                catch (Exception ex)
                {
                    session.Log($"WARNING: Could not set config file permissions: {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    session.Log("Config file created but with default permissions. Consider setting permissions manually for security.");
                    // Continue - permissions are a hardening measure, not critical for installation
                }

                // Store generated values for summary dialog
                session.Log("Storing generated values for summary dialog...");
                try
                {
                    session["CONFIG_APIKEY_GENERATED"] = apiKey;
                    session["CONFIG_AGENTID_GENERATED"] = agentId;
                    session.Log("Values stored successfully");
                }
                catch (Exception ex)
                {
                    session.Log($"WARNING: Could not store values for summary dialog: {ex.GetType().Name}: {ex.Message}");
                    // Continue - this is not critical for installation
                }

                session.Log("=== ConfigCustomAction.CreateConfigFile completed successfully ===");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                // Log detailed error information
                if (session != null)
                {
                    session.Log($"ERROR: Exception in ConfigCustomAction.CreateConfigFile: {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        session.Log($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        session.Log($"Inner stack trace: {ex.InnerException.StackTrace}");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"ERROR: Exception in ConfigCustomAction.CreateConfigFile (session is null): {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                return ActionResult.Failure;
            }
        }

        /// <summary>
        /// Deletes ProgramData\Openctrol if requested during uninstall.
        /// </summary>
        [CustomAction]
        public static ActionResult DeleteProgramData(Session session)
        {
            try
            {
                session.Log("Begin ConfigCustomAction.DeleteProgramData");

                var data = session.CustomActionData;
                var deleteProgramData = data["DELETEPROGRAMDATA"] == "1";

                if (!deleteProgramData)
                {
                    session.Log("DeleteProgramData not requested, skipping");
                    return ActionResult.Success;
                }

                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var openctrolDir = Path.Combine(programData, "Openctrol");

                if (Directory.Exists(openctrolDir))
                {
                    Directory.Delete(openctrolDir, true);
                    session.Log($"Deleted {openctrolDir}");
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Error deleting ProgramData: {ex}");
                // Don't fail uninstall if deletion fails
                return ActionResult.Success;
            }
        }

        private static string GenerateRandomApiKey()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private static string EncryptWithDPAPI(string plainText)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(encryptedBytes);
        }
    }
}

