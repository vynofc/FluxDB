using System;

namespace FluxDB.Models
{
    /// <summary>
    /// License information
    /// </summary>
    public class LicenseInfo
    {
        public bool Valid { get; set; }
        public string LicenseKey { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string[] Features { get; set; }
        public DateTime LastChecked { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// License check request payload
    /// </summary>
    public class LicenseCheckRequest
    {
        public string LicenseKey { get; set; }
        public string DeviceId { get; set; }
        public string AppVersion { get; set; }
    }

    /// <summary>
    /// License check response from server
    /// </summary>
    public class LicenseCheckResponse
    {
        public bool Valid { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string[] Features { get; set; }
        public string Message { get; set; }
    }
}
