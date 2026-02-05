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
                txtMessage.Text = "Checking for updates...";
                LoggingService.Log("Startup: Checking for updates");
                var ok = await CheckForUpdatesAsync().ConfigureAwait(false);
                if (!ok)
                {
                    // Installer was started (or update process handled); exit app now.
                    Application.Current.Shutdown();
                    return;
                }

                // Switch back to UI thread for UI updates and license check
                await Dispatcher.BeginInvoke(new Action(() => txtMessage.Text = "Checking license..."));
                await EnsureFreeLicenseAsync().ConfigureAwait(false);

                // After ensuring license, attempt to upload indexes while still in splash
                try
                {
                    await Dispatcher.BeginInvoke(new Action(() => txtMessage.Text = "Uploading indexes..."));
                    var settings = _settingsService.Load();
                    if (!string.IsNullOrEmpty(settings.LicenseKey))
                    {
                        LoggingService.Log("Startup: Triggering upload of indexes from splash");
                        await _licenseService.UploadAllIndexesNowAsync(settings.LicenseKey).ConfigureAwait(false);
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

                await Dispatcher.BeginInvoke(new Action(() => txtMessage.Text = "Starting application..."));
                await Task.Delay(250).ConfigureAwait(false);

                // Show main window on UI thread
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    var main = new MainWindow();
                    main.Show();
                    Close();
                }));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Startup failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private int CompareVersionToAssembly(string remoteRaw, Version assemblyVer)
        {
            string Normalize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.Trim();
                // Strip labels starting with '!' (e.g. v0.1.5!beta -> v0.1.5)
                if (s.Contains("!")) s = s.Split('!')[0].Trim();
                if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
                return s;
            }

            var a = Normalize(remoteRaw);
            var aParts = a.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            // assemblyVer may be null, but normally not
            var bMajor = assemblyVer?.Major ?? 0;
            var bMinor = assemblyVer?.Minor ?? 0;
            var bBuild = assemblyVer?.Build >= 0 ? assemblyVer.Build : 0;
            var bRev = assemblyVer?.Revision >= 0 ? assemblyVer.Revision : 0;

            var max = Math.Max(aParts.Length, 4);

            for (int i = 0; i < max; i++)
            {
                int ai = 0;
                if (i < aParts.Length)
                {
                    string part = aParts[i];
                    // Handle version suffixes like -beta1 by taking only the numeric part (e.g. "4-beta1" -> "4")
                    if (part.Contains("-"))
                    {
                        part = part.Split('-')[0];
                    }
                    int.TryParse(part, out ai);
                }

                int bi = 0;
                switch (i)
                {
                    case 0: bi = bMajor; break;
                    case 1: bi = bMinor; break;
                    case 2: bi = bBuild; break;
                    case 3: bi = bRev; break;
                }

                if (ai < bi) return -1;
                if (ai > bi) return 1;
            }

            return 0;
        }

        private async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool skipUpdate = args.Any(a => a.Trim().ToLower() == "--noupdate");
                App.IsUpdateSkipped = skipUpdate;

                var exePath = Assembly.GetExecutingAssembly().Location;
                var exeDir = Path.GetDirectoryName(exePath) ?? ".";
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    var versionUrl = "https://nsce-cdn.fun/FluxDB/version.txt";
                    string remoteVersionText = null;
                    try
                    {
                        remoteVersionText = await http.GetStringAsync(versionUrl).ConfigureAwait(false);
                    }
                    catch { }

                    if (string.IsNullOrEmpty(remoteVersionText))
                        return true; // cannot check, continue

                    var parts = remoteVersionText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var remoteRaw = parts[parts.Length - 1].Trim();

                    // Strip installer labels starting with '!' (e.g. v0.1.5.4!beta -> v0.1.5.4)
                    if (remoteRaw.Contains("!"))
                    {
                        remoteRaw = remoteRaw.Split('!')[0].Trim();
                    }

                    // Compare semantic versions
                    var cmp = CompareVersionToAssembly(remoteRaw, assemblyVersion);
                    if (cmp <= 0)
                    {
                        // up-to-date
                        return true;
                    }

                    // New version available
                    App.IsUpdateAvailable = true;
                    App.AvailableVersion = remoteRaw;

                    if (skipUpdate)
                    {
                        LoggingService.Log("Update available but --noupdate flag is set. Skipping installer.");
                        return true; // Continue app startup
                    }

                    // expected zip name MUST match exactly the remote string (without .zip)
                    var zipName = $"{remoteRaw}.zip";
                    var zipPath = Path.Combine("C:\\nsce\\FluxDB", zipName);

                    // If the expected zip is missing, attempt to run installer (from that folder) or download it
                    LoggingService.Log($"Expected zip path: {zipPath} exists={File.Exists(zipPath)}");
                    if (!File.Exists(zipPath))
                    {
                        // If there is an installer in that folder, prefer starting it from there
                        var installerInFolder = Path.Combine("C:\\nsce\\FluxDB", "FluxDB-Installer.exe");
                        if (File.Exists(installerInFolder))
                        {
                            var startInfo = new ProcessStartInfo(installerInFolder)
                            {
                                WorkingDirectory = Path.GetDirectoryName(installerInFolder),
                                UseShellExecute = true
                            };
                            Process.Start(startInfo);
                            return false; // exit app after starting installer
                        }

                        LoggingService.Log("Downloading installer as fallback");
                        // Otherwise download and run installer
                        return await DownloadAndRunInstallerAsync(exeDir).ConfigureAwait(false);
                    }

                    if (File.Exists(zipPath))
                    {
                        // If there is an installer in that folder, prefer starting it from there
                        var installerInFolder = Path.Combine("C:\\nsce\\FluxDB", "FluxDB-Installer.exe");
                        if (File.Exists(installerInFolder))
                        {
                            var startInfo = new ProcessStartInfo(installerInFolder)
                            {
                                WorkingDirectory = Path.GetDirectoryName(installerInFolder),
                                UseShellExecute = true
                            };
                            Process.Start(startInfo);
                            return false; // exit app after starting installer
                        }

                        // Otherwise fall back to running any installer next to the exe (existing behavior)
                        return await RunInstallerAsync(exeDir).ConfigureAwait(false);
                    }
                    else
                    {
                        // Download installer executable and run
                        return await DownloadAndRunInstallerAsync(exeDir).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                return true; // on error, continue startup
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
