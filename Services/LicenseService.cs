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
using System.Reflection;

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
            // Upload feature disabled by user request. Log and return.
            try
            {
                LoggingService.Log("Upload disabled: UploadAllIndexesIfNeededAsync called but uploads are turned off.");
            }
            catch { }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Public trigger to start uploading indexes if the current stored license allows it.
        /// Disabled: does nothing now.
        /// </summary>
        public void TriggerUploadIfAllowed()
        {
            // Intentionally no-op: uploads have been disabled.
            LoggingService.Log("TriggerUploadIfAllowed called but uploads are disabled.");
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
        /// Disabled: will not perform uploads.
        /// </summary>
        public Task UploadAllIndexesNowAsync(string licenseKey = null)
        {
            try
            {
                LoggingService.Log("UploadAllIndexesNowAsync called but uploads are disabled.");
            }
            catch { }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get stored license key
        /// </summary>
        public string GetStoredLicenseKey()
        {
            try
            {
                return _settings.Load().LicenseKey;
            }
            catch { return null; }
        }

        /// <summary>
        /// Clear license
        /// </summary>
        public void ClearLicense()
        {
            try
            {
                var settings = _settings.Load();
                settings.LicenseKey = null;
                settings.LicenseValid = false;
                settings.LicenseExpiresAt = null;
                settings.LastLicenseCheck = null;
                settings.LicenseFeatures = null;
                _settings.Save(settings);
            }
            catch { }
        }

        /// <summary>
        /// Upload index - disabled (returns false)
        /// </summary>
        public Task<bool> UploadIndexAsync(string licenseKey, IndexExport index)
        {
            LoggingService.Log("UploadIndexAsync called but uploads are disabled.");
            return Task.FromResult(false);
        }

        private string GetAppVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }
    }
}
