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
                        var iconBmp = new BitmapImage();
                        iconBmp.BeginInit();
                        iconBmp.UriSource = iconUri;
                        iconBmp.CacheOption = BitmapCacheOption.OnLoad;
                        iconBmp.EndInit();
                        iconBmp.Freeze();
                        this.Icon = iconBmp;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = iconUri;
                        bmp.DecodePixelWidth = 180;
                        bmp.DecodePixelHeight = 180;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        imgLogo.Source = bmp;
                    }
                    catch (Exception ex) { LoggingService.Log($"SplashWindow: Failed to load icon: {ex.Message}"); }
                }
                else if (File.Exists(pngPath))
                {
                    try
                    {
                        var pngUri = new Uri(pngPath);
                        var iconBmp = new BitmapImage();
                        iconBmp.BeginInit();
                        iconBmp.UriSource = pngUri;
                        iconBmp.CacheOption = BitmapCacheOption.OnLoad;
                        iconBmp.EndInit();
                        iconBmp.Freeze();
                        this.Icon = iconBmp;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = pngUri;
                        bmp.DecodePixelWidth = 180;
                        bmp.DecodePixelHeight = 180;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
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

                txtMessage.Text = "Starting application...";
                await Task.Delay(150);

                var main = new MainWindow();
                Application.Current.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                main.Show();
                Application.Current.MainWindow = main;
                Application.Current.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                Hide();
                Close();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        LoggingService.Log("Startup: Checking for updates (background)");
                        var ok = await CheckForUpdatesAsync(autoInstall: settings.AutoUpdateCheck);
                        if (!ok)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                LoggingService.Log("Startup: Installer started, shutting down app");
                                Application.Current.Shutdown();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"Update check failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Startup CRITICAL failure: {ex}");
                MessageBox.Show("Startup failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
        }

        private async Task<bool> CheckForUpdatesAsync(bool autoInstall = true)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool skipUpdate = args.Any(a => a.Trim().Equals("--noupdate", StringComparison.OrdinalIgnoreCase));
                App.IsUpdateSkipped = skipUpdate;

                var localVersionStr = App.GetLocalVersion();
                var assembly = Assembly.GetExecutingAssembly();
                var exeDir = Path.GetDirectoryName(assembly.Location) ?? ".";

                if (LoggingService.IsDebugMode) LoggingService.LogDebug($"CheckForUpdatesAsync: localVersion={localVersionStr} exeDir={exeDir} skipUpdate={skipUpdate} autoInstall={autoInstall}");

                var releases = await FetchAllReleasesAsync();
                if (releases == null || releases.Count == 0) return true;

                var localVersion = VersionHelper.NormalizeVersion(localVersionStr);

                string latestStableTag = null;
                string latestStableVersion = null;
                string latestBetaTag = null;
                string latestBetaVersion = null;

                foreach (var rel in releases)
                {
                    var tag = rel.TagName;
                    var ver = VersionHelper.NormalizeVersion(tag);
                    var isPrerelease = rel.Prerelease;

                    if (!isPrerelease)
                    {
                        if (latestStableVersion == null || VersionHelper.CompareVersions(ver, latestStableVersion) > 0)
                        {
                            latestStableVersion = ver;
                            latestStableTag = tag;
                        }
                    }
                    else
                    {
                        if (latestBetaVersion == null || VersionHelper.CompareVersions(ver, latestBetaVersion) > 0)
                        {
                            latestBetaVersion = ver;
                            latestBetaTag = tag;
                        }
                    }
                }

                bool stableNewer = latestStableVersion != null && VersionHelper.CompareVersions(latestStableVersion, localVersion) > 0;
                bool betaNewer = latestBetaVersion != null && VersionHelper.CompareVersions(latestBetaVersion, localVersion) > 0;

                if (!stableNewer && !betaNewer) return true;

                App.IsUpdateAvailable = true;
                App.AvailableVersion = stableNewer ? latestStableVersion : latestBetaVersion;
                App.AvailableTag = stableNewer ? latestStableTag : latestBetaTag;
                App.IsBetaUpdate = !stableNewer && betaNewer;
                App.AvailableBetaVersion = betaNewer ? latestBetaVersion : null;

                if (!autoInstall)
                {
                    LoggingService.Log($"Update available ({App.AvailableTag}) but auto-install is disabled.");
                    return true;
                }

                if (localVersionStr.EndsWith("-debug", StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.Log($"Update available ({App.AvailableTag}) but local build is a debug build. Skipping auto-install.");
                    return true;
                }

                if (App.IsBetaUpdate)
                {
                    LoggingService.Log($"Beta update available ({latestBetaTag}), skipping auto-install.");
                    return true;
                }

                if (skipUpdate)
                {
                    LoggingService.Log("Update available but --noupdate flag is set. Skipping.");
                    return true;
                }

                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");
                if (!File.Exists(installerPath))
                {
                    var ok = await UpdateService.DownloadInstallerAsync(exeDir, latestStableTag);
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
                LoggingService.Log($"Update check error: {ex}");
                return true;
            }
        }

        private async Task<List<GitHubRelease>> FetchAllReleasesAsync()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxDB");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    var response = await http.GetAsync("https://api.github.com/repos/vynofc/FluxDB/releases?per_page=20");
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<GitHubRelease>>(json);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"FetchAllReleasesAsync failed: {ex.Message}");
                return null;
            }
        }
    }
}
