using Microsoft.Deployment.WindowsInstaller;

namespace Openctrol.Agent.Setup.CustomActions
{
    public class ValidationCustomAction
    {
        /// <summary>
        /// Validates configuration input from the installer UI.
        /// </summary>
        [CustomAction]
        public static ActionResult ValidateConfig(Session session)
        {
            try
            {
                session.Log("Begin ValidationCustomAction.ValidateConfig");

                var port = session["CONFIG_PORT"];
                var useHttps = session["CONFIG_USEHTTPS"] == "1";
                var certPath = session["CONFIG_CERTPATH"] ?? "";
                var certPassword = session["CONFIG_CERTPASSWORD"] ?? "";

                // Validate port
                if (string.IsNullOrEmpty(port) || !int.TryParse(port, out var portNum))
                {
                    session["VALIDCONFIG"] = "0";
                    session["VALIDCONFIGERROR"] = "Port must be a valid number";
                    return ActionResult.Success;
                }

                if (portNum < 1024 || portNum > 65535)
                {
                    session["VALIDCONFIG"] = "0";
                    session["VALIDCONFIGERROR"] = "Port must be between 1024 and 65535";
                    return ActionResult.Success;
                }

                // Validate HTTPS configuration
                if (useHttps)
                {
                    if (string.IsNullOrEmpty(certPath))
                    {
                        session["VALIDCONFIG"] = "0";
                        session["VALIDCONFIGERROR"] = "Certificate path is required when using HTTPS";
                        return ActionResult.Success;
                    }

                    if (!System.IO.File.Exists(certPath))
                    {
                        session["VALIDCONFIG"] = "0";
                        session["VALIDCONFIGERROR"] = "Certificate file not found";
                        return ActionResult.Success;
                    }

                    if (string.IsNullOrEmpty(certPassword))
                    {
                        session["VALIDCONFIG"] = "0";
                        session["VALIDCONFIGERROR"] = "Certificate password is required";
                        return ActionResult.Success;
                    }
                }

                session["VALIDCONFIG"] = "1";
                session.Log("Configuration validation passed");
                return ActionResult.Success;
            }
            catch (System.Exception ex)
            {
                session.Log($"Error in validation: {ex}");
                session["VALIDCONFIG"] = "0";
                session["VALIDCONFIGERROR"] = "Validation error occurred";
                return ActionResult.Success;
            }
        }

        /// <summary>
        /// Generates a random API key and sets it in the CONFIG_APIKEY property.
        /// </summary>
        [CustomAction]
        public static ActionResult GenerateApiKey(Session session)
        {
            try
            {
                session.Log("Begin ValidationCustomAction.GenerateApiKey");

                var bytes = new byte[32];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(bytes);
                }
                var apiKey = System.Convert.ToBase64String(bytes)
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .Replace("=", "");

                session["CONFIG_APIKEY"] = apiKey;
                session.Log("API key generated");
                return ActionResult.Success;
            }
            catch (System.Exception ex)
            {
                session.Log($"Error generating API key: {ex}");
                return ActionResult.Success;
            }
        }
    }
}

