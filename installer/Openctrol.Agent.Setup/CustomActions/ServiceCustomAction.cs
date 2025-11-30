using System;
using System.IO;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Deployment.WindowsInstaller;

namespace Openctrol.Agent.Setup.CustomActions
{
    public class ServiceCustomAction
    {
        /// <summary>
        /// Starts the OpenctrolAgent service after installation.
        /// This runs with elevated privileges.
        /// </summary>
        [CustomAction]
        public static ActionResult InstallService(Session session)
        {
            try
            {
                session.Log("Begin ServiceCustomAction.InstallService");

                var data = session.CustomActionData;
                var installFolder = data["INSTALLFOLDER"];
                var serviceName = data["SERVICENAME"];

                var serviceExe = Path.Combine(installFolder, "Openctrol.Agent.exe");

                session.Log($"Service executable: {serviceExe}");
                session.Log($"Service name: {serviceName}");

                // Wait for service installation to complete and service to be registered
                // ServiceInstall happens in InstallServices, but registration may take a moment
                var maxRetries = 10;
                var retryDelay = 500; // milliseconds
                ServiceController sc = null;
                
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        sc = new ServiceController(serviceName);
                        // If we can get the service controller, the service is registered
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        // Service not found yet, wait and retry
                        if (i < maxRetries - 1)
                        {
                            Thread.Sleep(retryDelay);
                        }
                        else
                        {
                            session.Log($"Warning: Service {serviceName} not found after {maxRetries} retries. Service may need to be started manually.");
                            return ActionResult.Success; // Don't fail installation
                        }
                    }
                }

                // Start the service if we have a valid controller
                if (sc != null)
                {
                    try
                    {
                        using (sc)
                        {
                            var status = sc.Status;
                            if (status != ServiceControllerStatus.Running)
                            {
                                session.Log($"Starting service (current status: {status})...");
                                sc.Start();
                                
                                // Wait for service to start, with timeout
                                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                                
                                session.Log("Service started successfully");
                            }
                            else
                            {
                                session.Log("Service is already running");
                            }
                        }
                    }
                    catch (System.TimeoutException ex)
                    {
                        session.Log($"Warning: Service start timed out after 30 seconds: {ex.Message}");
                        session.Log("The service may need to be started manually. Check Windows Event Log for errors.");
                        // Don't fail installation - user can start service manually
                    }
                    catch (Exception ex)
                    {
                        session.Log($"Warning: Could not start service: {ex.GetType().Name}: {ex.Message}");
                        session.Log("The service may need to be started manually. Check Windows Event Log and config.json for errors.");
                        // Don't fail installation if service start fails - user can start it manually
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"Error in ServiceCustomAction: {ex}");
                // Don't fail installation - service might already be running or user can start manually
                return ActionResult.Success;
            }
        }
    }
}

