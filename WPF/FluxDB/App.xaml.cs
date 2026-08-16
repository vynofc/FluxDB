using System;
using System.Windows;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;

namespace FluxDB
{
    public partial class App : Application
    {
        public static bool IsUpdateAvailable { get; set; } = false;
        public static string AvailableVersion { get; set; } = "";
        public static string AvailableTag { get; set; } = "";
        public static string AvailableBetaVersion { get; set; } = null;
        public static bool IsUpdateSkipped { get; set; } = false;
        public static bool IsBetaUpdate { get; set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                Services.LoggingService.Log($"Unhandled dispatcher exception: {args.Exception}");
                MessageBox.Show("An unexpected error occurred:\n" + args.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Services.LoggingService.Log($"Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    Services.LoggingService.Log($"Unhandled domain exception: {ex}");
            };

            var ver = GetLocalVersion();
            bool debugMode = ver.EndsWith("-debug", StringComparison.OrdinalIgnoreCase);
#if DEBUG
            debugMode = true;
#endif
            FluxDB.Services.LoggingService.SetDebugMode(debugMode);
            if (debugMode)
                FluxDB.Services.LoggingService.Log($"DEBUG MODE ACTIVE — version: {ver}");
        }

        public static string GetLocalVersion()
        {
            string maxVer = "0.0.0";
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var exePath = assembly.Location;
                var exeDir = Path.GetDirectoryName(exePath);

                var localFile = Path.Combine(exeDir ?? "", "version.txt");
                if (File.Exists(localFile))
                {
                    try
                    {
                        var ver = File.ReadAllText(localFile).Trim();
                        if (!string.IsNullOrEmpty(ver) && VersionHelper.CompareVersions(ver, maxVer) > 0)
                            maxVer = ver;
                    }
                    catch (Exception ex)
                    {
                        Services.LoggingService.Log($"Error reading local version.txt: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Services.LoggingService.Log($"Error in GetLocalVersion: {ex.Message}");
            }

            try
            {
                var ass = Assembly.GetExecutingAssembly();
                var inf = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(ass, typeof(AssemblyInformationalVersionAttribute));
                var assVer = inf?.InformationalVersion ?? ass.GetName().Version.ToString();
                if (VersionHelper.CompareVersions(assVer, maxVer) > 0)
                    maxVer = assVer;
            }
            catch (Exception ex)
            {
                Services.LoggingService.Log($"Error reading Assembly version: {ex.Message}");
            }

            return maxVer;
        }
    }
}