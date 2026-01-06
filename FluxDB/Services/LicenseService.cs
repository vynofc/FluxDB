using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluxDB.Models;
using Newtonsoft.Json;

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

        public LicenseService(SettingsService settings, string baseUrl = "https://your-nextjs-site.com/api")
        {
            _settings = settings;
            _licenseEndpoint = $"{baseUrl}/license/check";
            _uploadEndpoint = $"{baseUrl}/license/upload";
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
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
                        LastChecked = settings.LastLicenseCheck.Value
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

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_licenseEndpoint, content);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var licenseResponse = JsonConvert.DeserializeObject<LicenseCheckResponse>(responseJson);
                    
                    // Update cache
                    settings.LicenseKey = licenseKey;
                    settings.LicenseValid = licenseResponse.Valid;
                    settings.LicenseExpiresAt = licenseResponse.ExpiresAt;
                    settings.LastLicenseCheck = DateTime.Now;
                    _settings.Save(settings);

                    return new LicenseInfo
                    {
                        Valid = licenseResponse.Valid,
                        LicenseKey = licenseKey,
                        ExpiresAt = licenseResponse.ExpiresAt,
                        Features = licenseResponse.Features,
                        LastChecked = DateTime.Now
                    };
                }
                else
                {
                    return new LicenseInfo
                    {
                        Valid = false,
                        LicenseKey = licenseKey,
                        ErrorMessage = $"Server returned: {response.StatusCode}",
                        LastChecked = DateTime.Now
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                // Offline - use cached value if available
                if (settings.LastLicenseCheck.HasValue && settings.LicenseKey == licenseKey)
                {
                    return new LicenseInfo
                    {
                        Valid = settings.LicenseValid,
                        LicenseKey = licenseKey,
                        ExpiresAt = settings.LicenseExpiresAt,
                        LastChecked = settings.LastLicenseCheck.Value,
                        ErrorMessage = "Offline - using cached license"
                    };
                }

                return new LicenseInfo
                {
                    Valid = false,
                    LicenseKey = licenseKey,
                    ErrorMessage = $"Connection failed: {ex.Message}",
                    LastChecked = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                return new LicenseInfo
                {
                    Valid = false,
                    LicenseKey = licenseKey,
                    ErrorMessage = $"Error: {ex.Message}",
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
                    licenseKey,
                    deviceId = GetDeviceId(),
                    indexVersion = index.Version,
                    content = index
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_uploadEndpoint, content);
                return response.IsSuccessStatusCode;
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
