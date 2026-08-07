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

namespace FluxDB.Views
{
    public partial class SplashWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly SettingsService _settingsService = new SettingsService();

        public SplashWindow()
        {
            InitializeComponent();
            Loaded += SplashWindow_Loaded;
            btnCancel.Click += (s, e) => { Application.Current.Shutdown(); };

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

                var main = new MainWindow();
                Application.Current.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                main.Show();
                Hide();
                Close();
                Application.Current.MainWindow = main;
                Application.Current.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
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