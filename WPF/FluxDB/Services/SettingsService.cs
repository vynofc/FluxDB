using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    /// <summary>
    /// Registry of all developer settings (dotted keys with short descriptions and defaults).
    /// </summary>
    public static class DevSettingsRegistry
    {
        public const string SearchDebounceKey = "input.search.time";
        public const string PreviewMaxCharsKey = "preview.text.maxchars";
        public const string NavigationHistorySizeKey = "navigation.history.size";
        public const string RecentFoldersMaxKey = "folders.recent.max";
        public const string LogBufferLinesKey = "log.buffer.lines";
        public const string ImageZoomMaxKey = "preview.image.zoommax";
        public const string IndexerBatchSizeKey = "indexer.batch.size";

        public static readonly List<DevSettingDefinition> All = new List<DevSettingDefinition>
        {
            new DevSettingDefinition { Key = SearchDebounceKey, DefaultValue = "250", Description = "Verzögerung in ms, bevor die Suche nach der Eingabe startet." },
            new DevSettingDefinition { Key = PreviewMaxCharsKey, DefaultValue = "5000", Description = "Maximale Zeichenanzahl, die in der Textvorschau angezeigt wird." },
            new DevSettingDefinition { Key = NavigationHistorySizeKey, DefaultValue = "50", Description = "Maximale Anzahl an Einträgen im Navigationsverlauf (Zurück/Vorwärts)." },
            new DevSettingDefinition { Key = RecentFoldersMaxKey, DefaultValue = "10", Description = "Maximale Anzahl gespeicherter zuletzt geöffneter Ordner." },
            new DevSettingDefinition { Key = LogBufferLinesKey, DefaultValue = "2000", Description = "Maximale Zeilen im Log-Speicherpuffer." },
            new DevSettingDefinition { Key = ImageZoomMaxKey, DefaultValue = "10", Description = "Maximaler Zoom-Faktor für die Bildvorschau." },
            new DevSettingDefinition { Key = IndexerBatchSizeKey, DefaultValue = "1000", Description = "Anzahl Dateien pro Datenbank-Transaktion beim Indizieren." },
        };

        public static string GetDefault(string key)
        {
            foreach (var def in All)
            {
                if (def.Key == key) return def.DefaultValue;
            }
            return null;
        }
    }

    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class SettingsService
    {
        private const string AppDataFolderName = "FluxDB";
        private const string SettingsFileName = "settings.json";

        private readonly string _settingsPath;
        private readonly object _fileLock = new object();
        private AppSettings _cachedSettings;
        private bool _saveDirty;
        private CancellationTokenSource _saveCts;

        public SettingsService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDataFolderName
            );

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _settingsPath = Path.Combine(appDataPath, SettingsFileName);
        }

        /// <summary>
        /// Get the application data directory
        /// </summary>
        public string GetAppDataDirectory()
        {
            return Path.GetDirectoryName(_settingsPath);
        }

        /// <summary>
        /// Load settings from file
        /// </summary>
        public AppSettings Load()
        {
            lock (_fileLock)
            {
                if (_cachedSettings != null)
                    return _cachedSettings;

                try
                {
                    if (File.Exists(_settingsPath))
                    {
                        var json = File.ReadAllText(_settingsPath);
                        _cachedSettings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                        _cachedSettings.DeviceId = DeviceIdentityService.GetOrCreateDeviceId();
                        return _cachedSettings;
                    }
                }
                catch
                {
                    // Return default settings on error
                }

                _cachedSettings = new AppSettings();
                _cachedSettings.DeviceId = DeviceIdentityService.GetOrCreateDeviceId();
                return _cachedSettings;
            }
        }

        /// <summary>
        /// Save settings to file (debounced, in-memory cache updated immediately)
        /// </summary>
        public void Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                _cachedSettings = settings;
                _saveDirty = true;
            }

            _saveCts?.Cancel();
            _saveCts = new CancellationTokenSource();
            var ct = _saveCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, ct);
                    if (ct.IsCancellationRequested) return;
                    Flush();
                }
                catch (OperationCanceledException) { }
            }, ct);
        }

        /// <summary>
        /// Immediately write any pending settings to disk
        /// </summary>
        public void Flush()
        {
            AppSettings toWrite;
            lock (_fileLock)
            {
                if (!_saveDirty || _cachedSettings == null) return;
                toWrite = _cachedSettings;
                _saveDirty = false;
            }

            try
            {
                var json = JsonConvert.SerializeObject(toWrite, Formatting.Indented);
                File.WriteAllText(_settingsPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a developer setting value (string), falling back to the registry default.
        /// </summary>
        public string GetDevSetting(string key)
        {
            try
            {
                var settings = Load();
                if (settings.DevSettings != null && settings.DevSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch
            {
                // Fall through to default
            }
            return DevSettingsRegistry.GetDefault(key);
        }

        /// <summary>
        /// Get a developer setting as int, falling back to the registry default on parse errors.
        /// </summary>
        public int GetDevSettingInt(string key)
        {
            var raw = GetDevSetting(key);
            if (int.TryParse(raw, out var result) && result > 0)
                return result;
            var def = DevSettingsRegistry.GetDefault(key);
            if (int.TryParse(def, out var defResult) && defResult > 0)
                return defResult;
            return 0;
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
                var persistence = settings.Persistence ?? new PersistenceOptions();

                if (persistence.RecentFolders)
                {
                    if (settings.RecentFolders == null)
                        settings.RecentFolders = new System.Collections.Generic.List<string>();

                    // Remove if already exists (case-insensitive)
                    settings.RecentFolders.RemoveAll(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));

                    // Add to beginning
                    settings.RecentFolders.Insert(0, normalized);

                    // Keep only last N
                    var maxRecent = GetDevSettingInt(DevSettingsRegistry.RecentFoldersMaxKey);
                    if (maxRecent <= 0) maxRecent = 10;
                    if (settings.RecentFolders.Count > maxRecent)
                    {
                        settings.RecentFolders.RemoveRange(maxRecent, settings.RecentFolders.Count - maxRecent);
                    }
                }

                if (persistence.LastRootFolder)
                {
                    settings.LastRootFolder = normalized;
                }

                Save(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddRecentFolder failed: {ex.Message}");
            }
        }
    }
}
