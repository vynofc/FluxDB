using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FluxDB.Services;

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
                    catch { }
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
                    catch { }
                }
            }
            catch { }
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

        private string NormalizeVersion(string s)
        {
            return VersionHelper.NormalizeVersion(s);
        }

        private int CompareVersions(string v1, string v2)
        {
            return VersionHelper.CompareVersions(v1, v2);
        }

        private async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool skipUpdate = args.Any(a => a.Trim().Equals("--noupdate", StringComparison.OrdinalIgnoreCase));
                App.IsUpdateSkipped = skipUpdate;

                var localVersionStr = App.GetLocalVersion();
                bool isLocalBeta = localVersionStr.Contains("-");

                var assembly = Assembly.GetExecutingAssembly();
                var exePath = assembly.Location;
                var exeDir = Path.GetDirectoryName(exePath) ?? ".";
                
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    var versionUrl = "https://nsce-cdn.fun/FluxDB/version.txt";
                    string remoteVersionText = null;
                    try
                    {
                        remoteVersionText = await http.GetStringAsync(versionUrl).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"Update check: Failed to download version.txt: {ex.Message}");
                    }

                    if (string.IsNullOrEmpty(remoteVersionText))
                        return true;

                    var parts = remoteVersionText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    string newestStableRaw = null;
                    string newestOverallRaw = null;

                    foreach (var part in parts)
                    {
                        var raw = part.Trim();
                        bool isRemoteBeta = raw.Contains("!beta");

                        if (newestOverallRaw == null || CompareVersions(raw, newestOverallRaw) > 0)
                            newestOverallRaw = raw;

                        if (!isRemoteBeta)
                        {
                            if (newestStableRaw == null || CompareVersions(raw, newestStableRaw) > 0)
                                newestStableRaw = raw;
                        }
                    }

                    LoggingService.Log($"Update check: Stable={newestStableRaw}, Overall={newestOverallRaw}, Local={localVersionStr}");

                    string targetUpdateRaw = null;

                    // Logic:
                    // 1. If a newer Stable is available -> Force Update to newest Stable
                    if (newestStableRaw != null && CompareVersions(newestStableRaw, localVersionStr) > 0)
                    {
                        targetUpdateRaw = newestStableRaw;
                    }
                    // 2. If a newer Overall (Beta) is available
                    else if (newestOverallRaw != null && CompareVersions(newestOverallRaw, localVersionStr) > 0)
                    {
                        if (isLocalBeta)
                        {
                            // If we are already on a beta, force update to newest available (next beta or release)
                            targetUpdateRaw = newestOverallRaw;
                        }
                        else
                        {
                            // We are on Stable, and there is only a newer Beta available -> Optional
                            App.IsBetaUpdateAvailable = true;
                            App.AvailableBetaVersion = NormalizeVersion(newestOverallRaw);
                            return true;
                        }
                    }

                    if (targetUpdateRaw == null)
                    {
                        // Up to date
                        return true;
                    }

                    // New version available for force update
                    var finalRemoteRaw = NormalizeVersion(targetUpdateRaw);
                    App.IsUpdateAvailable = true;
                    App.AvailableVersion = finalRemoteRaw;

                    if (skipUpdate)
                    {
                        LoggingService.Log("Update available but --noupdate flag is set. Skipping installer.");
                        return true;
                    }

                    // Zip name should match what's in version.txt (without !beta suffix)
                    var zipName = targetUpdateRaw.Split('!')[0].Trim() + ".zip";
                    var zipPath = Path.Combine("C:\\NSCE\\FluxDB", zipName);
                    LoggingService.Log($"Expected zip path: {zipPath} exists={File.Exists(zipPath)}");

                    // Start installer
                    var installerInFolder = Path.Combine("C:\\NSCE\\FluxDB", "FluxDB-Installer.exe");
                    if (File.Exists(installerInFolder))
                    {
                        var startInfo = new ProcessStartInfo(installerInFolder)
                        {
                            WorkingDirectory = Path.GetDirectoryName(installerInFolder),
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        return false; 
                    }

                    return await RunInstallerAsync(exeDir).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Update check error: {ex.Message}");
                return true;
            }
        }

        private async Task<bool> RunInstallerAsync(string exeDir)
        {
            try
            {
                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");
                if (!File.Exists(installerPath))
                {
                    return await DownloadAndRunInstallerAsync(exeDir).ConfigureAwait(false);
                }

                var start = new ProcessStartInfo(installerPath)
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = true
                };
                Process.Start(start);
                return false; // we started installer; exit app
            }
            catch
            {
                return true; // on failure, continue
            }
        }

        private async Task<bool> DownloadAndRunInstallerAsync(string exeDir)
        {
            try
            {
                var installerUrl = "https://nsce-cdn.fun/FluxDB/FluxDB-Installer.exe";
                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");

                using (var http = new HttpClient())
                using (var resp = await http.GetAsync(installerUrl).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                        return true;

                    using (var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                    }
                }

                var start = new ProcessStartInfo(installerPath)
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = true
                };
                Process.Start(start);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
