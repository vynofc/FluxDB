using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows.Media;
using Newtonsoft.Json;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluxDB
{
    public partial class App : Application
    {
        public static bool IsUpdateAvailable { get; set; }
        public static string AvailableVersion { get; set; } = "";
        public static bool IsUpdateSkipped { get; set; }

        private IHost _host;

        public static IHost Host => ((App)Current)._host;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var ver = GetLocalVersion();
            bool debugMode = ver.EndsWith("-debug", StringComparison.OrdinalIgnoreCase);
            Services.LoggingService.SetDebugMode(debugMode);
            if (debugMode)
                Services.LoggingService.Log($"DEBUG MODE ACTIVE — version: {ver}");

            _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddFluxDB();
                })
                .Build();

            ApplyTheme();

            var splash = new SplashWindow();
            splash.Show();
        }

        private void ApplyTheme()
        {
            var settingsService = _host.Services.GetRequiredService<Services.SettingsService>();
            var settings = settingsService.Load();
            var theme = settings.Theme switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark
            };
            ApplicationThemeManager.Apply(theme);
            ApplicationThemeManager.ApplySystemTheme();
        }

        public static void ToggleTheme()
        {
            var currentTheme = ApplicationThemeManager.GetAppTheme();
            var newTheme = currentTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
            ApplicationThemeManager.Apply(newTheme);

            var settingsService = Host.Services.GetRequiredService<Services.SettingsService>();
            var settings = settingsService.Load();
            settings.Theme = newTheme == ApplicationTheme.Light ? "Light" : "Dark";
            settingsService.Save(settings);
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            Services.LoggingService.Shutdown();
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
                    catch (Exception ex) { Services.LoggingService.Log($"Error reading local version.txt: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Services.LoggingService.Log($"Error in GetLocalVersion: {ex.Message}"); }

            try
            {
                var ass = Assembly.GetExecutingAssembly();
                var inf = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(ass, typeof(AssemblyInformationalVersionAttribute));
                var assVer = inf?.InformationalVersion ?? ass.GetName().Version.ToString();
                if (VersionHelper.CompareVersions(assVer, maxVer) > 0)
                    maxVer = assVer;
            }
            catch (Exception ex) { Services.LoggingService.Log($"Error reading Assembly version: {ex.Message}"); }

            return maxVer;
        }
    }
}