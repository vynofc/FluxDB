using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FluxDB
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    public partial class App : Application
    {
        public static bool IsUpdateAvailable { get; set; } = false;
        public static string AvailableVersion { get; set; } = "";
        public static bool IsUpdateSkipped { get; set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var ver = GetLocalVersion();
            bool debugMode = ver.EndsWith("-debug", StringComparison.OrdinalIgnoreCase);
            FluxDB.Services.LoggingService.SetDebugMode(debugMode);
            if (debugMode)
                FluxDB.Services.LoggingService.Log($"DEBUG MODE ACTIVE — version: {ver}");
        }

        public static string GetLocalVersion()
        {
            string maxVer = "0.0.0";
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var exePath = assembly.Location;
                var exeDir = System.IO.Path.GetDirectoryName(exePath);

                // 1. Try local version.txt in app folder
                var localFile = System.IO.Path.Combine(exeDir ?? "", "version.txt");
                if (System.IO.File.Exists(localFile))
                {
                    try
                    {
                        var ver = System.IO.File.ReadAllText(localFile).Trim();
                        if (!string.IsNullOrEmpty(ver) && VersionHelper.CompareVersions(ver, maxVer) > 0) maxVer = ver;
                    }
                    catch (Exception ex)
                    {
                        FluxDB.Services.LoggingService.Log($"Error reading local version.txt: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FluxDB.Services.LoggingService.Log($"Error in GetLocalVersion: {ex.Message}");
            }

            // 2. Fallback to Assembly version
            try
            {
                var ass = System.Reflection.Assembly.GetExecutingAssembly();
                var inf = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(ass, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                var assVer = inf?.InformationalVersion ?? ass.GetName().Version.ToString();
                if (VersionHelper.CompareVersions(assVer, maxVer) > 0) maxVer = assVer;
            }
            catch (Exception ex)
            {
                FluxDB.Services.LoggingService.Log($"Error reading Assembly version: {ex.Message}");
            }

            return maxVer;
        }
    }
}
