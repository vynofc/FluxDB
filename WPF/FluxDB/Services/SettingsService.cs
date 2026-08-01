using System;
using System.IO;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsPath;

        public SettingsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluxDB"
            );

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _settingsPath = Path.Combine(appDataPath, "settings.json");
        }

        /// <summary>
        /// Get the application data directory
        /// </summary>
        public string GetAppDataDirectory()
        {
            return Path.GetDirectoryName(_settingsPath);
        }

        /// <summary>
        /// Get the database path
        /// </summary>
        public string GetDatabasePath()
        {
            return Path.Combine(GetAppDataDirectory(), "fluxdb.db");
        }

        /// <summary>
        /// Load settings from file
        /// </summary>
        public AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Return default settings on error
            }

            return new AppSettings();
        }

        /// <summary>
        /// Save settings to file
        /// </summary>
        public void Save(AppSettings settings)
        {
            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a folder to recent folders
        /// </summary>
        public void AddRecentFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;

            try
            {
                // Normalize to full path without trailing separator
                var normalized = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var settings = Load();

                if (settings.RecentFolders == null)
                    settings.RecentFolders = new System.Collections.Generic.List<string>();

                // Remove if already exists (case-insensitive)
                settings.RecentFolders.RemoveAll(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));

                // Add to beginning
                settings.RecentFolders.Insert(0, normalized);

                // Keep only last 10
                if (settings.RecentFolders.Count > 10)
                {
                    settings.RecentFolders.RemoveRange(10, settings.RecentFolders.Count - 10);
                }

                settings.LastRootFolder = normalized;
                Save(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddRecentFolder failed: {ex.Message}");
            }
        }
    }
}
