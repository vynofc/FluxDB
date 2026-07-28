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
        public static bool IsBetaUpdateAvailable { get; set; } = false;
        public static string AvailableBetaVersion { get; set; } = "";
        public static bool IsUpdateSkipped { get; set; } = false;

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

                // 2. Try central version.txt
                var centralDir = Environment.GetEnvironmentVariable("FLUXDB_CENTRAL_DIR") ?? "C:\\NSCE\\FluxDB";
                var centralFile = System.IO.Path.Combine(centralDir, "version.txt");
                if (System.IO.File.Exists(centralFile))
                {
                    try
                    {
                        var ver = System.IO.File.ReadAllText(centralFile).Trim();
                        if (!string.IsNullOrEmpty(ver) && VersionHelper.CompareVersions(ver, maxVer) > 0) maxVer = ver;
                    }
                    catch (Exception ex)
                    {
                        FluxDB.Services.LoggingService.Log($"Error reading central version.txt: {ex.Message}");
                    }
                }

                // 3. Try highest version zip in FLUXDB_CENTRAL_DIR
                if (System.IO.Directory.Exists(centralDir))
                {
                    try
                    {
                        var zips = System.IO.Directory.GetFiles(centralDir, "*.zip");
                        foreach (var zip in zips)
                        {
                            var name = System.IO.Path.GetFileNameWithoutExtension(zip);
                            if (!string.IsNullOrEmpty(name) && VersionHelper.CompareVersions(name, maxVer) > 0) maxVer = name;
                        }
                    }
                    catch (Exception ex)
                    {
                        FluxDB.Services.LoggingService.Log($"Error scanning central ZIPs: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FluxDB.Services.LoggingService.Log($"Error in GetLocalVersion: {ex.Message}");
            }

            // 4. Fallback to Assembly
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
