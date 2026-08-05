using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Wpf.Ui.Controls;

namespace FluxDB.Views
{
    public partial class SplashWindow : Window
    {
        private readonly SettingsService _settingsService = new Services.SettingsService();

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var icoPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.ico");
                var pngPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.png");

                if (File.Exists(icoPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(icoPath);
                    bmp.DecodePixelWidth = 120;
                    bmp.DecodePixelHeight = 120;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgLogo.Source = bmp;
                }
                else if (File.Exists(pngPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pngPath);
                    bmp.DecodePixelWidth = 120;
                    bmp.DecodePixelHeight = 120;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgLogo.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"SplashWindow: Failed to load icon: {ex.Message}");
            }

            txtVersion.Text = App.GetLocalVersion();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var settings = _settingsService.Load();
                if (settings.AutoUpdateCheck)
                {
                    try
                    {
                        txtMessage.Text = "Checking for updates...";
                        LoggingService.Log("Startup: Checking for updates");
                        var ok = await CheckForUpdatesAsync();
                        if (!ok)
                        {
                            LoggingService.Log("Startup: Installer started, shutting down app");
                            Application.Current.Shutdown();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"Update check failed: {ex.Message}");
                    }
                }
                else
                {
                    LoggingService.Log("Startup: AutoUpdateCheck disabled, skipping update check");
                }

                txtMessage.Text = "Starting application...";
                await Task.Delay(250);

                var mainWindow = App.Host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Startup CRITICAL failure: {ex.Message}");
                System.Windows.MessageBox.Show("Startup failed: " + ex.Message, "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool skipUpdate = args.Any(a => a.Trim().Equals("--noupdate", StringComparison.OrdinalIgnoreCase));
                App.IsUpdateSkipped = skipUpdate;

                var localVersionStr = App.GetLocalVersion();
                var assembly = Assembly.GetExecutingAssembly();
                var exeDir = Path.GetDirectoryName(assembly.Location) ?? ".";

                LoggingService.LogDebug($"CheckForUpdatesAsync: localVersion={localVersionStr} exeDir={exeDir} skipUpdate={skipUpdate}");

                var latestTag = await FetchLatestReleaseTagAsync();
                if (latestTag == null) return true;

                var remoteVersion = VersionHelper.NormalizeVersion(latestTag);
                var localVersion = VersionHelper.NormalizeVersion(localVersionStr);
                var cmp = VersionHelper.CompareVersions(remoteVersion, localVersion);

                if (cmp <= 0) return true;

                App.IsUpdateAvailable = true;
                App.AvailableVersion = remoteVersion;

                if (skipUpdate)
                {
                    LoggingService.Log("Update available but --noupdate flag is set. Skipping.");
                    return true;
                }

                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");
                if (!File.Exists(installerPath))
                {
                    var ok = await DownloadInstallerAsync(exeDir, latestTag);
                    if (!ok) return true;
                }

                var startInfo = new ProcessStartInfo(installerPath, "--silent-start")
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = true
                };
                using (var proc = Process.Start(startInfo)) { }
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Update check error: {ex.Message}");
                return true;
            }
        }

        private async Task<string> FetchLatestReleaseTagAsync()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxDB");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    var response = await http.GetAsync("https://api.github.com/repos/vynofc/FluxDB/releases/latest");
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    return release?.TagName;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"FetchLatestReleaseTagAsync failed: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> DownloadInstallerAsync(string exeDir, string tag)
        {
            try
            {
                var url = $"https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB-Installer.exe";
                var destPath = Path.Combine(exeDir, "FluxDB-Installer.exe");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    var bytes = await http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(destPath, bytes);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DownloadInstallerAsync failed: {ex.Message}");
                return false;
            }
        }
    }
}