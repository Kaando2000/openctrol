using System;
using System.Diagnostics;
using Microsoft.Deployment.WindowsInstaller;

namespace Openctrol.Agent.Setup.CustomActions
{
    public class FirewallCustomAction
    {
        /// <summary>
        /// Creates a Windows Firewall inbound rule for the Openctrol Agent port.
        /// </summary>
        [CustomAction]
        public static ActionResult CreateFirewallRule(Session session)
        {
            try
            {
                session.Log("Begin FirewallCustomAction.CreateFirewallRule");

                var data = session.CustomActionData;
                var port = data["PORT"];
                var createFirewall = data["CREATEFIREWALL"] == "1";

                if (!createFirewall)
                {
                    session.Log("Firewall rule creation not requested");
                    return ActionResult.Success;
                }

                var ruleName = "Openctrol Agent";
                var portNum = int.Parse(port);

                session.Log($"Creating firewall rule: {ruleName} for port {portNum}");

                // Use netsh to create firewall rule
                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={portNum}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();

                        if (process.ExitCode == 0)
                        {
                            session.Log($"Firewall rule created successfully: {output}");
                        }
                        else
                        {
                            session.Log($"Warning: Firewall rule creation returned exit code {process.ExitCode}: {error}");
                            // Don't fail installation if firewall rule creation fails
                        }
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Error creating firewall rule: {ex}");
                // Don't fail installation if firewall rule creation fails
                return ActionResult.Success;
            }
        }

        /// <summary>
        /// Removes the Windows Firewall rule during uninstall.
        /// </summary>
        [CustomAction]
        public static ActionResult RemoveFirewallRule(Session session)
        {
            try
            {
                session.Log("Begin FirewallCustomAction.RemoveFirewallRule");

                var ruleName = "Openctrol Agent";

                session.Log($"Removing firewall rule: {ruleName}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        
                        if (process.ExitCode == 0)
                        {
                            session.Log($"Firewall rule '{ruleName}' removed successfully");
                        }
                        else
                        {
                            // Exit code 1 usually means rule not found, which is fine during uninstall
                            if (output.Contains("No rules match") || error.Contains("No rules match"))
                            {
                                session.Log($"Firewall rule '{ruleName}' not found (may have been removed already)");
                            }
                            else
                            {
                                session.Log($"Warning: Firewall rule removal returned exit code {process.ExitCode}: {error}");
                            }
                        }
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Error removing firewall rule: {ex}");
                // Don't fail uninstall if firewall rule removal fails
                return ActionResult.Success;
            }
        }
    }
}

