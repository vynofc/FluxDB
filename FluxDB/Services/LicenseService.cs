using System;
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
        private readonly SettingsService _settings;
        private readonly HttpClient _httpClient;
        private readonly string _verifyEndpoint;
        private readonly string _uploadEndpoint;

        public LicenseService(SettingsService settings, string baseUrl = "https://fluxdb.nsce.fr/api")
        {
            _settings = settings;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _verifyEndpoint = $"{baseUrl}/license/verify";
            _uploadEndpoint = $"{baseUrl}/index/upload";
        }

        /// <summary>
        /// Get current device ID (8-128 characters)
        /// </summary>
        public string GetDeviceId()
        {
            var settings = _settings.Load();
            if (string.IsNullOrEmpty(settings.DeviceId))
            {
                settings.DeviceId = GenerateDeviceId();
                _settings.Save(settings);
            }
            return settings.DeviceId;
        }

        private string GenerateDeviceId()
        {
            // Generate a unique device ID (32 chars, within 8-128 range)
            var machineInfo = Environment.MachineName + Environment.UserName + Environment.ProcessorCount + DateTime.Now.Ticks;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32).ToLower();
            }
        }

        /// <summary>
        /// Check license status (uses cache if valid)
        /// POST /api/license/verify
        /// </summary>
        public async Task<LicenseInfo> CheckLicenseAsync(string licenseKey, bool forceRefresh = false)
        {
            var settings = _settings.Load();

            // Return cached if still valid and not forcing refresh
            if (!forceRefresh && settings.LicenseValid && settings.LicenseKey == licenseKey)
            {
                if (settings.LastLicenseCheck.HasValue)
                {
                    var hoursSinceCheck = (DateTime.Now - settings.LastLicenseCheck.Value).TotalHours;
                    if (hoursSinceCheck < 24) // Cache for 24 hours
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
            }

            try
            {
                // API: POST /api/license/verify
                var payload = new
                {
                    licenseKey = licenseKey,
                    deviceId = GetDeviceId(),
                    appVersion = GetAppVersion()
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_verifyEndpoint, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LicenseVerifyResponse>(responseJson);

                if (result.Valid)
                {
                    // Save to settings
                    settings.LicenseKey = licenseKey;
                    settings.LicenseValid = true;
                    settings.LicenseExpiresAt = result.ExpiresAt;
                    settings.LastLicenseCheck = DateTime.Now;
                    _settings.Save(settings);

                    return new LicenseInfo
                    {
                        Valid = true,
                        LicenseKey = licenseKey,
                        ExpiresAt = result.ExpiresAt,
                        LastChecked = DateTime.Now
                    };
                }
                else
                {
                    // Invalid license
                    settings.LicenseValid = false;
                    _settings.Save(settings);

                    return new LicenseInfo
                    {
                        Valid = false,
                        LicenseKey = licenseKey,
                        ErrorMessage = result.Error ?? "License invalid",
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
        /// POST /api/index/upload
        /// </summary>
        public async Task<UploadResult> UploadIndexAsync(string licenseKey, IndexExport index)
        {
            try
            {
                // API: POST /api/index/upload
                var payload = new
                {
                    licenseKey = licenseKey,
                    deviceId = GetDeviceId(),
                    version = 1,
                    indexJson = index
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_uploadEndpoint, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<UploadResponse>(responseJson);

                if (result.Ok)
                {
                    return new UploadResult
                    {
                        Success = true,
                        Message = "Upload successful",
                        IndexId = result.IndexId
                    };
                }
                else
                {
                    return new UploadResult
                    {
                        Success = false,
                        Message = result.Error ?? $"Upload failed: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new UploadResult
                {
                    Success = false,
                    Message = $"Upload error: {ex.Message}"
                };
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

        // Response classes for JSON deserialization
        private class LicenseVerifyResponse
        {
            [JsonProperty("valid")]
            public bool Valid { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; }

            [JsonProperty("expiresAt")]
            public DateTime? ExpiresAt { get; set; }

            [JsonProperty("features")]
            public LicenseFeatures Features { get; set; }
        }

        private class LicenseFeatures
        {
            [JsonProperty("upload")]
            public bool Upload { get; set; }
        }

        private class UploadResponse
        {
            [JsonProperty("ok")]
            public bool Ok { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; }

            [JsonProperty("indexId")]
            public string IndexId { get; set; }
        }
    }

    /// <summary>
    /// Result of an upload operation
    /// </summary>
    public class UploadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string IndexId { get; set; }
    }
}
