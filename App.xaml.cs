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
                        if (!string.IsNullOrEmpty(ver) && CompareVersionsInternal(ver, maxVer) > 0) maxVer = ver;
                    }
                    catch (Exception ex)
                    {
                        FluxDB.Services.LoggingService.Log($"Error reading local version.txt: {ex.Message}");
                    }
                }

                // 2. Try central version.txt
                var centralFile = "C:\\NSCE\\FluxDB\\version.txt";
                if (System.IO.File.Exists(centralFile))
                {
                    try
                    {
                        var ver = System.IO.File.ReadAllText(centralFile).Trim();
                        if (!string.IsNullOrEmpty(ver) && CompareVersionsInternal(ver, maxVer) > 0) maxVer = ver;
                    }
                    catch (Exception ex)
                    {
                        FluxDB.Services.LoggingService.Log($"Error reading central version.txt: {ex.Message}");
                    }
                }

                // 3. Try highest version zip in C:\NSCE\FluxDB
                var centralDir = "C:\\NSCE\\FluxDB";
                if (System.IO.Directory.Exists(centralDir))
                {
                    try
                    {
                        var zips = System.IO.Directory.GetFiles(centralDir, "*.zip");
                        foreach (var zip in zips)
                        {
                            var name = System.IO.Path.GetFileNameWithoutExtension(zip);
                            if (!string.IsNullOrEmpty(name) && CompareVersionsInternal(name, maxVer) > 0) maxVer = name;
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
                if (CompareVersionsInternal(assVer, maxVer) > 0) maxVer = assVer;
            }
            catch (Exception ex)
            {
                FluxDB.Services.LoggingService.Log($"Error reading Assembly version: {ex.Message}");
            }

            return maxVer;
        }

        private static string NormalizeVersionInternal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            if (s.Contains("!")) s = s.Split('!')[0].Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            return s.Trim();
        }

        private static int CompareVersionsInternal(string v1, string v2)
        {
            var s1 = NormalizeVersionInternal(v1);
            var s2 = NormalizeVersionInternal(v2);

            if (s1 == s2) return 0;

            var p1 = s1.Split('-');
            var p2 = s2.Split('-');

            if (Version.TryParse(p1[0], out Version ver1) && Version.TryParse(p2[0], out Version ver2))
            {
                int cmp = ver1.CompareTo(ver2);
                if (cmp != 0) return cmp;
            }

            bool hasSuffix1 = p1.Length > 1;
            bool hasSuffix2 = p2.Length > 1;

            if (!hasSuffix1 && hasSuffix2) return 1;
            if (hasSuffix1 && !hasSuffix2) return -1;

            if (hasSuffix1 && hasSuffix2)
            {
                return string.Compare(p1[1], p2[1], StringComparison.OrdinalIgnoreCase);
            }

            return 0;
        }
    }
}
