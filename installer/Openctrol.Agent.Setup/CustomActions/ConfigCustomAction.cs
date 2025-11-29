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
                session.Log("Begin ConfigCustomAction.CreateConfigFile");

                // Parse custom action data
                var data = session.CustomActionData;
                var installFolder = data["INSTALLFOLDER"];
                var port = data["PORT"];
                var useHttps = data["USEHTTPS"] == "1";
                var certPath = data["CERTPATH"] ?? "";
                var certPassword = data["CERTPASSWORD"] ?? "";
                var apiKey = data["APIKEY"] ?? "";
                var agentId = data["AGENTID"] ?? "";

                session.Log($"Install folder: {installFolder}");
                session.Log($"Port: {port}");
                session.Log($"Use HTTPS: {useHttps}");
                session.Log($"API Key provided: {!string.IsNullOrEmpty(apiKey)}");
                session.Log($"Agent ID provided: {!string.IsNullOrEmpty(agentId)}");

                // Generate API key if not provided
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = GenerateRandomApiKey();
                    session.Log("Generated new API key");
                }

                // Generate Agent ID if not provided
                if (string.IsNullOrEmpty(agentId))
                {
                    agentId = Guid.NewGuid().ToString();
                    session.Log("Generated new Agent ID");
                }

                // Encrypt certificate password if provided
                var certPasswordEncrypted = "";
                if (!string.IsNullOrEmpty(certPassword))
                {
                    try
                    {
                        certPasswordEncrypted = EncryptWithDPAPI(certPassword);
                        session.Log("Certificate password encrypted");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"Error encrypting certificate password: {ex.Message}");
                        return ActionResult.Failure;
                    }
                }

                // ProgramData path
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var configDir = Path.Combine(programData, "Openctrol");
                var configPath = Path.Combine(configDir, "config.json");

                // Check if config already exists
                if (File.Exists(configPath))
                {
                    session.Log($"Config file already exists at {configPath}, skipping creation");
                    
                    // Read actual values from existing config file
                    try
                    {
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
                        session.Log($"Warning: Could not read existing config file: {ex.Message}. Summary may show incorrect values.");
                        // Fallback to generated values if we can't read the file
                        session["CONFIG_APIKEY_GENERATED"] = apiKey;
                        session["CONFIG_AGENTID_GENERATED"] = agentId;
                    }
                    
                    return ActionResult.Success;
                }

                // Create directory if it doesn't exist
                Directory.CreateDirectory(configDir);

                // Create config object matching AgentConfig structure
                var config = new Dictionary<string, object>
                {
                    { "AgentId", agentId },
                    { "HttpPort", int.Parse(port) },
                    { "MaxSessions", 1 },
                    { "CertPath", certPath },
                    { "CertPasswordEncrypted", certPasswordEncrypted },
                    { "TargetFps", 30 },
                    { "AllowedHaIds", new List<string>() },
                    { "ApiKey", apiKey }
                };

                // Serialize to JSON
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                // Write config file
                File.WriteAllText(configPath, json, Encoding.UTF8);

                // Set restrictive permissions (Administrators and SYSTEM only)
                // This is important for security as the config contains API keys and encrypted passwords
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
                    session.Log("Config file permissions set (Administrators and SYSTEM only)");
                }
                catch (UnauthorizedAccessException ex)
                {
                    session.Log($"Warning: Insufficient privileges to set config file permissions: {ex.Message}");
                    session.Log("Config file created but with default permissions. Consider setting permissions manually for security.");
                    // Continue - permissions are a hardening measure, not critical for installation
                }
                catch (Exception ex)
                {
                    session.Log($"Warning: Could not set config file permissions: {ex.GetType().Name}: {ex.Message}");
                    session.Log("Config file created but with default permissions. Consider setting permissions manually for security.");
                    // Continue - permissions are a hardening measure, not critical for installation
                }

                // Store generated values for summary dialog
                session["CONFIG_APIKEY_GENERATED"] = apiKey;
                session["CONFIG_AGENTID_GENERATED"] = agentId;

                session.Log("Config file created successfully");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Error in ConfigCustomAction: {ex}");
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

