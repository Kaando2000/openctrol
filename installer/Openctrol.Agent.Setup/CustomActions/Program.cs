using System;
using System.Linq;
using Microsoft.Deployment.WindowsInstaller;

namespace Openctrol.Agent.Setup.CustomActions
{
    /// <summary>
    /// Main entry point for EXE custom actions.
    /// Windows Installer calls this EXE with the custom action name.
    /// The session handle is obtained from the environment variable MSIINSTANCE.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: CustomActions.exe <CustomActionName>");
                return 1;
            }

            var customActionName = args[0];
            Session session = null;

            try
            {
                // Get session handle from environment variable (Windows Installer sets this for EXE custom actions)
                // For deferred custom actions, MSIINSTANCE contains the session handle
                // For immediate custom actions, we need to use a different approach
                var msiInstance = Environment.GetEnvironmentVariable("MSIINSTANCE");
                if (!string.IsNullOrEmpty(msiInstance) && long.TryParse(msiInstance, out long handleLong))
                {
                    // ownsHandle=false because Windows Installer owns the handle
                    session = Session.FromHandle(new IntPtr(handleLong), ownsHandle: false);
                }
                else
                {
                    // For immediate custom actions, Windows Installer doesn't set MSIINSTANCE
                    // We need to use MsiGetActiveDatabase or get the session handle differently
                    // Try using the Session.Open method which works for immediate actions
                    try
                    {
                        // For immediate custom actions, we can open the active database
                        // and create a session from it, but this is complex
                        // Instead, let's try to get the handle from the installer process
                        // The simplest approach: use MsiGetActiveDatabase and create session from it
                        var dbPath = Environment.GetEnvironmentVariable("MSIOPENPACKAGE");
                        if (!string.IsNullOrEmpty(dbPath))
                        {
                            // We have the database path, but we need the session handle
                            // For immediate actions, we might need to pass the handle differently
                            // Let's check if there's a session handle in the environment
                            var allEnvVars = Environment.GetEnvironmentVariables();
                            foreach (var key in allEnvVars.Keys)
                            {
                                var keyStr = key.ToString();
                                if (keyStr.StartsWith("MSI") && keyStr.Contains("HANDLE"))
                                {
                                    var handleStr = allEnvVars[key].ToString();
                                    if (long.TryParse(handleStr, out long foundHandle))
                                    {
                                        session = Session.FromHandle(new IntPtr(foundHandle), ownsHandle: false);
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (session == null)
                        {
                            // Last resort: try to create a session from the active installer
                            // This uses internal Windows Installer APIs
                            // Note: This may not work for all scenarios
                            throw new InvalidOperationException($"Cannot get Windows Installer session handle for immediate custom action '{customActionName}'. MSIINSTANCE not set. This may indicate the custom action should be deferred.");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to get Windows Installer session: {ex.Message}. MSIINSTANCE={msiInstance}");
                    }
                }

                if (session == null)
                {
                    Console.Error.WriteLine($"Failed to get Windows Installer session for custom action: {customActionName}");
                    return 1;
                }

                session.Log($"Begin EXE custom action: {customActionName}");
                session.Log($"MSIINSTANCE environment variable: {Environment.GetEnvironmentVariable("MSIINSTANCE")}");

                // Route to appropriate custom action
                ActionResult result;
                try
                {
                    result = customActionName switch
                    {
                        "CreateConfigFile" => ConfigCustomAction.CreateConfigFile(session),
                        "InstallService" => ServiceCustomAction.InstallService(session),
                        "CreateFirewallRule" => FirewallCustomAction.CreateFirewallRule(session),
                        "RemoveFirewallRule" => FirewallCustomAction.RemoveFirewallRule(session),
                        "DeleteProgramData" => ConfigCustomAction.DeleteProgramData(session),
                        "ValidateConfig" => ValidationCustomAction.ValidateConfig(session),
                        "GenerateApiKey" => ValidationCustomAction.GenerateApiKey(session),
                        _ => ActionResult.Failure
                    };

                    session.Log($"Custom action '{customActionName}' completed with result: {result}");
                }
                catch (Exception ex)
                {
                    session.Log($"ERROR: Exception in custom action '{customActionName}': {ex.GetType().Name}: {ex.Message}");
                    session.Log($"Stack trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        session.Log($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                    result = ActionResult.Failure;
                }

                return result == ActionResult.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error executing custom action '{customActionName}': {ex}");
                if (session != null)
                {
                    session.Log($"Error in custom action '{customActionName}': {ex}");
                }
                return 1;
            }
        }
    }
}

