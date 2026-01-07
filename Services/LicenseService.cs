using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluxDB.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FluxDB.Services
{
    /// <summary>
    /// Service for license validation
    /// </summary>
    public class LicenseService
    {
        private readonly string _licenseEndpoint;
        private readonly string _uploadEndpoint;
        private readonly HttpClient _httpClient;
        private readonly SettingsService _settings;

        // Cache duration for license check (24 hours)
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public LicenseService(SettingsService settings, string baseUrl = "https://fluxdb.nsce.fr/api", bool allowInvalidSslCertificates = false)
        {
            _settings = settings;

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
                    _settings.Save(settings);

                    return new LicenseInfo
                    {
                        Valid = valid,
                        LicenseKey = licenseKey,
                        ExpiresAt = expiresAt,
                        Features = features,
                        LastChecked = DateTime.Now
                    };
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

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync(_uploadEndpoint, content).ConfigureAwait(false);
                }
                catch (HttpRequestException hex)
                {
                    var inner = hex.InnerException != null ? $" - {hex.InnerException.Message}" : string.Empty;
                    // Optionally log
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
}
