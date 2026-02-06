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
        private readonly LicenseService _licenseService;

        public SplashWindow()
        {
            InitializeComponent();
            _licenseService = new LicenseService(_settingsService);
            // Update splash message when upload reports status
            _licenseService.UploadStatusChanged += (msg) =>
            {
                try { Dispatcher.Invoke(() => txtMessage.Text = msg); } catch { }
            };
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

                // 2. License check
                try
                {
                    txtMessage.Text = "Checking license...";
                    LoggingService.Log("Startup: Checking license");
                    await EnsureFreeLicenseAsync();
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"License check failed: {ex.Message}");
                }

                // 3. Upload indexes
                try
                {
                    txtMessage.Text = "Uploading indexes...";
                    var settings = _settingsService.Load();
                    if (!string.IsNullOrEmpty(settings.LicenseKey))
                    {
                        LoggingService.Log("Startup: Triggering upload of indexes from splash");
                        await _licenseService.UploadAllIndexesNowAsync(settings.LicenseKey);
                        LoggingService.Log("Startup: Upload routine finished");
                    }
                    else
                    {
                        LoggingService.Log("Startup: No license key, skipping upload");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"Startup upload failed: {ex.Message}");
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
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            if (s.Contains("!")) s = s.Split('!')[0].Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            return s.Trim();
        }

        private int CompareVersions(string v1, string v2)
        {
            var s1 = NormalizeVersion(v1);
            var s2 = NormalizeVersion(v2);

            if (s1 == s2) return 0;

            var p1 = s1.Split('-');
            var p2 = s2.Split('-');

            if (Version.TryParse(p1[0], out Version ver1) && Version.TryParse(p2[0], out Version ver2))
            {
                int cmp = ver1.CompareTo(ver2);
                if (cmp != 0) return cmp;
            }

            // Base version same, check suffixes
            bool hasSuffix1 = p1.Length > 1;
            bool hasSuffix2 = p2.Length > 1;

            if (!hasSuffix1 && hasSuffix2) return 1;  // Release > Beta
            if (hasSuffix1 && !hasSuffix2) return -1; // Beta < Release

            if (hasSuffix1 && hasSuffix2)
            {
                // Simple string compare for beta suffixes (beta1 < beta2)
                return string.Compare(p1[1], p2[1], StringComparison.OrdinalIgnoreCase);
            }

            return 0;
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

        private async Task EnsureFreeLicenseAsync()
        {
            try
            {
                var settings = _settingsService.Load();
                if (!string.IsNullOrEmpty(settings.LicenseKey))
                    return; // already has license

                // request free license from API
                var deviceId = _licenseService.GetDeviceId();
                var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

                var payload = new { deviceId = deviceId, appVersion = appVersion };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    var url = "https://fluxdb.nsce.fr/api/license/free";
                    HttpResponseMessage resp = null;
                    try
                    {
                        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        resp = await http.PostAsync(url, content).ConfigureAwait(false);
                    }
                    catch { }

                    if (resp == null) return;
                    if (!resp.IsSuccessStatusCode) return;

                    var respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try
                    {
                        var j = Newtonsoft.Json.Linq.JObject.Parse(respJson);
                        var ok = j.Value<bool?>("ok") ?? false;
                        if (!ok) return;
                        var license = j["license"];
                        if (license == null) return;
                        var key = license.Value<string>("key");

                        if (!string.IsNullOrEmpty(key))
                        {
                            settings.LicenseKey = key;
                            settings.LicenseValid = true;
                            settings.LicenseExpiresAt = license.Value<DateTime?>("expiresAt");
                            settings.LastLicenseCheck = DateTime.Now;
                            // Mark as auto-generated so manual activations can override later
                            settings.IsAutoGeneratedFreeLicense = true;
                            _settingsService.Save(settings);

                            LoggingService.Log($"Saved free license: {key}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
