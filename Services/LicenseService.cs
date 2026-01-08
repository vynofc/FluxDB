using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluxDB.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FluxDB.Services
{
    /// <summary>
    /// Service for license validation
    /// </summary>
    public partial class LicenseService
    {
        private readonly string _licenseEndpoint;
        private readonly string _uploadEndpoint;
        private readonly HttpClient _httpClient;
        private readonly SettingsService _settings;
        private ExportService _exportService;

        // Upload status notifications
        public event Action<string> UploadStatusChanged;

        // Cache duration for license check (24 hours)
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public LicenseService(SettingsService settings, ExportService exportService = null, string baseUrl = "https://fluxdb.nsce.fr/api", bool allowInvalidSslCertificates = false)
        {
            _settings = settings;
            _exportService = exportService;

            // Ensure TLS 1.2 is enabled (helps with older runtime defaults)
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }

            _licenseEndpoint = $"{baseUrl}/license/verify"; // adjusted to spec
            _uploadEndpoint = $"{baseUrl}/index/upload"; // adjusted to spec

            // Optionally allow invalid/self-signed certs for development (use with caution)
            if (allowInvalidSslCertificates)
            {
                var handler = new HttpClientHandler();
#if NET472
                // ServerCertificateCustomValidationCallback is available; accept all certs if requested
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
                _httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }
            else
            {
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }
        }

        /// <summary>
        /// Allows setting ExportService later (e.g. when DB initialized)
        /// </summary>
        public void SetExportService(ExportService exportService)
        {
            _exportService = exportService;
        }

        /// <summary>
        /// Get current device ID
        /// </summary>
        public string GetDeviceId()
        {
            var settings = _settings.Load();

            if (string.IsNullOrEmpty(settings.DeviceId))
            {
                settings.DeviceId = Guid.NewGuid().ToString();
                _settings.Save(settings);
            }

            return settings.DeviceId;
        }

        /// <summary>
        /// Check license status (uses cache if valid)
        /// </summary>
        public async Task<LicenseInfo> CheckLicenseAsync(string licenseKey, bool forceRefresh = false)
        {
            var settings = _settings.Load();

            // Check cache first
            if (!forceRefresh && settings.LastLicenseCheck.HasValue)
            {
                var cacheAge = DateTime.Now - settings.LastLicenseCheck.Value;
                if (cacheAge < CacheDuration && settings.LicenseKey == licenseKey)
                {
                    return new LicenseInfo
                    {
                        Valid = settings.LicenseValid,
                        LicenseKey = licenseKey,
                        ExpiresAt = settings.LicenseExpiresAt,
                        LastChecked = settings.LastLicenseCheck,
                        Features = settings.LicenseFeatures ?? new System.Collections.Generic.Dictionary<string, bool>()
                    };
                }
            }

            // Make online check
            try
            {
                var request = new LicenseCheckRequest
                {
                    LicenseKey = licenseKey,
                    DeviceId = GetDeviceId(),
                    AppVersion = GetAppVersion()
                };

                // Serialize using camelCase so server receives fields named "licenseKey" and "deviceId"
                var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync(_licenseEndpoint, content).ConfigureAwait(false);
                }
                catch (HttpRequestException hex)
                {
                    // Include inner exception details when possible
                    var inner = hex.InnerException != null ? $" - {hex.InnerException.Message}" : string.Empty;
                    return new LicenseInfo
                    {
                        Valid = false,
                        LicenseKey = licenseKey,
                        ErrorMessage = $"Connection failed: {hex.Message}{inner}",
                        LastChecked = DateTime.Now
                    };
                }

                var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    // Try parse into known response shape
                    var j = JObject.Parse(responseJson);
                    var valid = j.Value<bool?>("valid") ?? false;
                    var expiresAt = j.Value<DateTime?>("expiresAt");
                    var featuresToken = j["features"];
                    var features = new System.Collections.Generic.Dictionary<string, bool>();

                    if (featuresToken != null && featuresToken.Type == JTokenType.Object)
                    {
                        foreach (var prop in (JObject)featuresToken)
                        {
                            bool val = false;
                            if (prop.Value.Type == JTokenType.Boolean)
                                val = prop.Value.Value<bool>();
                            else if (prop.Value.Type == JTokenType.String)
                                bool.TryParse(prop.Value.Value<string>(), out val);

                            features[prop.Key] = val;
                        }
                    }

                    // Update cache
                    settings.LicenseKey = licenseKey;
                    settings.LicenseValid = valid;
                    settings.LicenseExpiresAt = expiresAt;
                    settings.LastLicenseCheck = DateTime.Now;
                    settings.LicenseFeatures = features;
                    // A successful validation coming through this API indicates a user-provided or verified license
                    // mark it as not auto-generated so manual activation overrides free licenses
                    settings.IsAutoGeneratedFreeLicense = false;
                    _settings.Save(settings);

                    var info = new LicenseInfo
                    {
                        Valid = valid,
                        LicenseKey = licenseKey,
                        ExpiresAt = expiresAt,
                        Features = features,
                        LastChecked = DateTime.Now
                    };

                    // If upload feature enabled, trigger upload of existing indexes
                    if (IsUploadAllowed(features))
                    {
                        _ = Task.Run(() => UploadAllIndexesIfNeededAsync(licenseKey));
                    }

                    LoggingService.Log($"License check for {licenseKey}: success={response.IsSuccessStatusCode}");

                    return info;
                }
                else
                {
                    // Try to read error message from body
                    string err = null;
                    try
                    {
                        var j = JObject.Parse(responseJson);
                        err = j.Value<string>("error") ?? j.Value<string>("message");
                    }
                    catch { }

                    LoggingService.Log($"License check for {licenseKey}: success={response.IsSuccessStatusCode}");

                    return new LicenseInfo
                    {
                        Valid = false,
                        LicenseKey = licenseKey,
                        ErrorMessage = err ?? $"Server returned: {response.StatusCode}",
                        LastChecked = DateTime.Now
                    };
                }
            }
            catch (Exception ex)
            {
                // Catch-all with inner exception details
                var inner = ex.InnerException != null ? $" - {ex.InnerException.Message}" : string.Empty;
                return new LicenseInfo
                {
                    Valid = false,
                    LicenseKey = licenseKey,
                    ErrorMessage = $"Error: {ex.Message}{inner}",
                    LastChecked = DateTime.Now
                };
            }
        }

        private bool IsUploadAllowed(System.Collections.Generic.Dictionary<string, bool> features)
        {
            if (features == null) return false;
            if (features.TryGetValue("upload", out var allowed)) return allowed;
            return false;
        }

        private IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern)
        {
            var results = new List<string>();
            try
            {
                var dirs = new Stack<string>();
                dirs.Push(root);
                while (dirs.Count > 0)
                {
                    var dir = dirs.Pop();
                    try
                    {
                        foreach (var file in Directory.GetFiles(dir, searchPattern))
                        {
                            results.Add(file);
                        }
                    }
                    catch { }

                    try
                    {
                        foreach (var sub in Directory.GetDirectories(dir))
                        {
                            dirs.Push(sub);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return results;
        }

        private async Task UploadAllIndexesIfNeededAsync(string licenseKey)
        {
            try
            {
                var settings = _settings.Load();
                var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Include last root folder and recent folders
                if (!string.IsNullOrEmpty(settings.LastRootFolder) && Directory.Exists(settings.LastRootFolder))
                    candidateDirs.Add(Path.GetFullPath(settings.LastRootFolder));

                if (settings.RecentFolders != null)
                {
                    foreach (var f in settings.RecentFolders)
                    {
                        if (!string.IsNullOrEmpty(f) && Directory.Exists(f))
                            candidateDirs.Add(Path.GetFullPath(f));
                    }
                }

                // Include any directories previously tracked in UploadedIndexHashes (they may no longer be in RecentFolders)
                if (settings.UploadedIndexHashes != null)
                {
                    foreach (var kv in settings.UploadedIndexHashes.Keys)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(kv) && Directory.Exists(kv))
                                candidateDirs.Add(Path.GetFullPath(kv));
                        }
                        catch { }
                    }
                }

                // Also scan the app data directory for any .fluxdb files created by this app
                try
                {
                    var appDataDir = _settings.GetAppDataDirectory();
                    if (!string.IsNullOrEmpty(appDataDir) && Directory.Exists(appDataDir))
                    {
                        var dbFiles = Directory.GetFiles(appDataDir, "*.fluxdb", SearchOption.AllDirectories);
                        foreach (var db in dbFiles)
                        {
                            try
                            {
                                var dir = Path.GetDirectoryName(db);
                                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                    candidateDirs.Add(Path.GetFullPath(dir));
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // Additionally, scan all fixed drives for any .fluxdb files created anywhere
                try
                {
                    foreach (var drv in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        try
                        {
                            var root = drv.RootDirectory.FullName;
                            foreach (var db in SafeEnumerateFiles(root, "*.fluxdb"))
                            {
                                try
                                {
                                    var dir = Path.GetDirectoryName(db);
                                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                        candidateDirs.Add(Path.GetFullPath(dir));
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                 // Now check each candidate for a .fluxdb database file
                 var toUpload = new List<string>();
                 foreach (var dir in candidateDirs)
                 {
                     var dbPath = Path.Combine(dir, ".fluxdb");
                     if (File.Exists(dbPath)) toUpload.Add(dir);
                 }

                LoggingService.Log($"UploadAllIndexesIfNeededAsync started. Candidate dirs: {string.Join(",", candidateDirs)}");

                foreach (var dir in toUpload)
                {
                    try
                    {
                        var dbPath = Path.Combine(dir, ".fluxdb");
                        using (var tempDb = new DatabaseService(dbPath))
                        {
                            var tempExport = new ExportService(tempDb, _settings);
                            var export = tempExport.CreateExport(dir);
                            var json = JsonConvert.SerializeObject(export);
                            var hash = ComputeSha256Hash(json);

                            settings.UploadedIndexHashes.TryGetValue(dir, out var existingHash);
                            if (existingHash == hash) continue; // no change

                            var payload = new
                            {
                                licenseKey = licenseKey,
                                deviceId = GetDeviceId(),
                                version = export.Version,
                                indexJson = JsonConvert.DeserializeObject(json) // send object
                            };

                            var content = new StringContent(JsonConvert.SerializeObject(payload, new JsonSerializerSettings
                            {
                                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                                NullValueHandling = NullValueHandling.Ignore
                            }), Encoding.UTF8, "application/json");

                            LoggingService.Log($"Uploading index from {dir}");

                            // Notify upload started
                            UploadStatusChanged?.Invoke($"Uploading index from {dir}...");

                            // Use single-request upload (original behavior)
                            try
                            {
                                var resp = await _httpClient.PostAsync(_uploadEndpoint, content).ConfigureAwait(false);
                                var responseJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (resp.IsSuccessStatusCode)
                                {
                                    UploadStatusChanged?.Invoke($"Upload successful: {dir}");
                                    settings.UploadedIndexHashes[dir] = hash;
                                    _settings.Save(settings);
                                }
                                else
                                {
                                    string err = null;
                                    try { var j = JObject.Parse(responseJson); err = j.Value<string>("error") ?? j.Value<string>("message"); } catch { }
                                    UploadStatusChanged?.Invoke($"Upload failed (status {resp.StatusCode}): {err}");
                                }
                            }
                            catch (Exception ex)
                            {
                                UploadStatusChanged?.Invoke($"Upload failed: {ex.Message}");
                            }
                         }
                     }
                     catch (Exception ex)
                     {
                         // Notify exception
                         UploadStatusChanged?.Invoke($"Error uploading {dir}: {ex.Message}");
                         LoggingService.Log($"Error uploading {dir}: {ex.Message}");
                     }
                 }
             }
             catch (Exception ex)
             {
                 // Notify top-level error
                 UploadStatusChanged?.Invoke($"Error in upload process: {ex.Message}");
             }
         }

        private string ComputeSha256Hash(string raw)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(raw ?? "");
                var hash = sha.ComputeHash(bytes);
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }

        /// <summary>
        /// Upload index to server (only if license is valid)
        /// </summary>
        public async Task<bool> UploadIndexAsync(string licenseKey, IndexExport index)
        {
            try
            {
                var payload = new
                {
                    licenseKey = licenseKey,
                    deviceId = GetDeviceId(),
                    version = index.Version,
                    indexJson = index
                };

                var json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync(_uploadEndpoint, content).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    return false;
                }

                if (response.IsSuccessStatusCode)
                    return true;

                // try to log error body
                var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    var j = JObject.Parse(responseJson);
                    var err = j.Value<string>("error") ?? j.Value<string>("message");
                    // Could surface this to caller by changing signature; currently just return false
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if license is currently valid (from cache)
        /// </summary>
        public bool IsLicenseValid()
        {
            var settings = _settings.Load();

            if (!settings.LicenseValid) return false;
            if (!settings.LicenseExpiresAt.HasValue) return settings.LicenseValid;

            return settings.LicenseExpiresAt.Value > DateTime.Now;
        }

        /// <summary>
        /// Get stored license key
        /// </summary>
        public string GetStoredLicenseKey()
        {
            return _settings.Load().LicenseKey;
        }

        /// <summary>
        /// Clear license
        /// </summary>
        public void ClearLicense()
        {
            var settings = _settings.Load();
            settings.LicenseKey = null;
            settings.LicenseValid = false;
            settings.LicenseExpiresAt = null;
            settings.LastLicenseCheck = null;
            settings.LicenseFeatures = null;
            _settings.Save(settings);
        }

        private string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }
    }

    public partial class LicenseService
    {
        /// <summary>
        /// Public trigger to start uploading indexes if the current stored license allows it.
        /// </summary>
        public void TriggerUploadIfAllowed()
        {
            try
            {
                var settings = _settings.Load();
                var key = settings.LicenseKey;
                if (string.IsNullOrEmpty(key)) return;

                if (IsUploadAllowed(settings.LicenseFeatures))
                {
                    _ = Task.Run(() => UploadAllIndexesIfNeededAsync(key));
                }
            }
            catch { }
        }

        /// <summary>
        /// Returns true if the currently stored license was auto-generated by the app (free license).
        /// </summary>
        public bool IsStoredLicenseAutoGenerated()
        {
            try
            {
                var settings = _settings.Load();
                return settings.IsAutoGeneratedFreeLicense;
            }
            catch { return false; }
        }

        /// <summary>
        /// Public method to trigger upload of all indexes now. If licenseKey is null, uses stored license in settings.
        /// </summary>
        public async Task UploadAllIndexesNowAsync(string licenseKey = null)
        {
            var settings = _settings.Load();
            var keyToUse = licenseKey ?? settings.LicenseKey;
            if (string.IsNullOrEmpty(keyToUse))
            {
                throw new InvalidOperationException("No license key provided or stored.");
            }

            // Directly call upload routine
            await UploadAllIndexesIfNeededAsync(keyToUse).ConfigureAwait(false);
        }
    }
}
