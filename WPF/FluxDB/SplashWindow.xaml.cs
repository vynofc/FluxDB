using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FluxDB.Models;
using FluxDB.Services;
using Newtonsoft.Json;

namespace FluxDB
{
    public partial class SplashWindow : Window
    {
        private readonly SettingsService _settingsService = new SettingsService();

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += SplashWindow_Loaded;
            btnCancel.Click += (s, e) => { Application.Current.Shutdown(); };

            // Load icon if present in application folder (FluxDB-icon.ico or PNG)
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var icoPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.ico");
                var pngPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.png");

                if (File.Exists(icoPath))
                {
                    try
                    {
                        var iconUri = new Uri(icoPath);
                        this.Icon = BitmapFrame.Create(iconUri);

                        // .ico may contain multiple sizes; create BitmapImage for display
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = iconUri;
                        bmp.DecodePixelWidth = 180;
                        bmp.DecodePixelHeight = 180;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgLogo.Source = bmp;
                    }
                    catch (Exception ex) { LoggingService.Log($"SplashWindow: Failed to load icon: {ex.Message}"); }
                }
                else if (File.Exists(pngPath))
                {
                    try
                    {
                        var pngUri = new Uri(pngPath);
                        // Set window icon from PNG as well
                        this.Icon = BitmapFrame.Create(pngUri);

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = pngUri;
                        bmp.DecodePixelWidth = 180;
                        bmp.DecodePixelHeight = 180;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgLogo.Source = bmp;
                    }
                    catch (Exception ex) { LoggingService.Log($"SplashWindow: Failed to load icon: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LoggingService.Log($"SplashWindow: Failed to load icon: {ex.Message}"); }
        }

        private async void SplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Check for updates
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

                // 4. Show main window
                var main = new MainWindow();
                main.Show();
                Close();
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Startup CRITICAL failure: {ex.Message}");
                MessageBox.Show("Startup failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.Invoke(() => Application.Current.Shutdown());
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
                LoggingService.LogDebug($"CheckForUpdatesAsync: latestTag={latestTag ?? "(null — fetch failed)"}");
                if (latestTag == null)
                    return true;

                var remoteVersion = VersionHelper.NormalizeVersion(latestTag);
                var localVersion = VersionHelper.NormalizeVersion(localVersionStr);
                var cmp = VersionHelper.CompareVersions(remoteVersion, localVersion);

                LoggingService.LogDebug($"CheckForUpdatesAsync: remoteVersion={remoteVersion} localVersion={localVersion} compareResult={cmp}");

                if (cmp <= 0)
                {
                    LoggingService.LogDebug("CheckForUpdatesAsync: no update needed");
                    return true;
                }

                App.IsUpdateAvailable = true;
                App.AvailableVersion = remoteVersion;
                LoggingService.LogDebug($"CheckForUpdatesAsync: update available {localVersion} \u2192 {remoteVersion}");

                if (skipUpdate)
                {
                    LoggingService.Log("Update available but --noupdate flag is set. Skipping.");
                    return true;
                }

                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");
                LoggingService.LogDebug($"CheckForUpdatesAsync: installerPath={installerPath} exists={File.Exists(installerPath)}");
                if (!File.Exists(installerPath))
                {
                    LoggingService.LogDebug("CheckForUpdatesAsync: downloading installer...");
                    var ok = await DownloadInstallerAsync(exeDir, latestTag);
                    LoggingService.LogDebug($"CheckForUpdatesAsync: installer download result={ok}");
                    if (!ok) return true;
                }

                LoggingService.LogDebug($"CheckForUpdatesAsync: launching installer, shutting down app");
                var startInfo = new ProcessStartInfo(installerPath, "--silent-start")
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
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

                    LoggingService.LogDebug("FetchLatestReleaseTagAsync: GET https://api.github.com/repos/vynofc/FluxDB/releases/latest");
                    var response = await http.GetAsync("https://api.github.com/repos/vynofc/FluxDB/releases/latest");
                    LoggingService.LogDebug($"FetchLatestReleaseTagAsync: HTTP {(int)response.StatusCode} {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    LoggingService.LogDebug($"FetchLatestReleaseTagAsync: response body={json}");
                    var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    LoggingService.LogDebug($"FetchLatestReleaseTagAsync: deserialized TagName={release?.TagName}");
                    return release?.TagName;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"GitHub API error: {ex.GetType().Name}: {ex.Message}");
                LoggingService.LogDebug($"FetchLatestReleaseTagAsync FAILED — {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> DownloadInstallerAsync(string exeDir, string tag)
        {
            try
            {
                var url = $"https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB-Installer.exe";
                var path = Path.Combine(exeDir, "FluxDB-Installer.exe");

                using (var http = new HttpClient())
                using (var resp = await http.GetAsync(url))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        await resp.Content.CopyToAsync(fs);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
