using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    public static class UpdateService
    {
        public static async Task<string> FetchLatestReleaseTagAsync()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxDB");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    var response = await http.GetAsync("https://api.github.com/repos/vynofc/FluxDB/releases/latest");
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    return release?.TagName;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"FetchLatestReleaseTagAsync failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> DownloadInstallerAsync(string exeDir, string tag)
        {
            try
            {
                var url = $"https://github.com/vynofc/FluxDB/releases/download/{Uri.EscapeDataString(tag)}/FluxDB-Installer.exe";
                var destPath = Path.Combine(exeDir, "FluxDB-Installer.exe");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    var bytes = await http.GetByteArrayAsync(url);

                    var verified = await VerifyInstallerChecksumAsync(http, url, bytes);
                    if (!verified)
                    {
                        LoggingService.Log("DownloadInstallerAsync: checksum mismatch, aborting install");
                        return false;
                    }

                    await File.WriteAllBytesAsync(destPath, bytes);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DownloadInstallerAsync failed: {ex}");
                return false;
            }
        }

        private static async Task<bool> VerifyInstallerChecksumAsync(HttpClient http, string installerUrl, byte[] installerBytes)
        {
            string expectedHash;
            try
            {
                expectedHash = (await http.GetStringAsync(installerUrl + ".sha256"))?.Trim();
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Checksum file not available, skipping verification: {ex.Message}");
                return true;
            }

            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                LoggingService.Log("Checksum file is empty, skipping verification");
                return true;
            }

            expectedHash = expectedHash.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var actualHash = BitConverter.ToString(sha.ComputeHash(installerBytes)).Replace("-", "");
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.Log($"Checksum mismatch: expected={expectedHash} actual={actualHash}");
                    return false;
                }
            }
            return true;
        }
    }
}
