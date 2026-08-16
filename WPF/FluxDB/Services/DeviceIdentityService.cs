using System;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace FluxDB.Services
{
    /// <summary>
    /// Provides a persistent, pseudonymous device ID stored in three locations
    /// (HKCU registry, device.id file, settings.json cache). Read order:
    /// registry, device.id, settings.json, then generate a new UUIDv7.
    /// </summary>
    public static class DeviceIdentityService
    {
        private const string RegistryKeyPath = @"Software\FluxDB";
        private const string RegistryValueName = "DeviceId";
        private const string AppDataFolderName = "FluxDB";
        private const string DeviceIdFileName = "device.id";
        private const string SettingsFileName = "settings.json";

        private static readonly object _lock = new object();
        private static string _cachedDeviceId;

        private static readonly string _deviceIdFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName,
            DeviceIdFileName);

        private static readonly string _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName,
            SettingsFileName);

        /// <summary>
        /// Returns the device ID, creating and persisting a new UUIDv7 on first use.
        /// Never throws; falls back to an in-memory ID if all storage fails.
        /// </summary>
        public static string GetOrCreateDeviceId()
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedDeviceId))
                    return _cachedDeviceId;

                var id = ReadFromRegistry();
                if (id != null)
                {
                    _cachedDeviceId = id;
                    return id;
                }

                id = ReadFromDeviceIdFile();
                if (id != null)
                {
                    WriteToRegistry(id);
                    _cachedDeviceId = id;
                    return id;
                }

                id = ReadFromSettingsJson();
                if (id != null)
                {
                    WriteToRegistry(id);
                    WriteToDeviceIdFile(id);
                    _cachedDeviceId = id;
                    return id;
                }

                id = Guid.CreateVersion7().ToString();
                WriteToRegistry(id);
                WriteToDeviceIdFile(id);
                WriteToSettingsJson(id);
                LoggingService.Log($"Generated new device ID: {id}");
                _cachedDeviceId = id;
                return id;
            }
        }

        /// <summary>
        /// Deletes the device ID from registry, device.id and settings.json.
        /// A new ID is generated on the next call to <see cref="GetOrCreateDeviceId"/>.
        /// </summary>
        public static void ResetDeviceId()
        {
            lock (_lock)
            {
                _cachedDeviceId = null;

                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                    {
                        key?.DeleteValue(RegistryValueName, false);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"Failed to delete device ID from registry: {ex.Message}");
                }

                try
                {
                    if (File.Exists(_deviceIdFilePath))
                        File.Delete(_deviceIdFilePath);
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"Failed to delete device.id file: {ex.Message}");
                }

                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                        var obj = JObject.Parse(json);
                        if (obj.Remove(RegistryValueName))
                            File.WriteAllText(_settingsFilePath, obj.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"Failed to remove device ID from settings.json: {ex.Message}");
                }

                LoggingService.Log("Device ID was reset");
            }
        }

        private static string ReadFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    var value = key?.GetValue(RegistryValueName) as string;
                    if (IsValidGuid(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to read device ID from registry: {ex.Message}");
            }
            return null;
        }

        private static string ReadFromDeviceIdFile()
        {
            try
            {
                if (File.Exists(_deviceIdFilePath))
                {
                    var value = File.ReadAllText(_deviceIdFilePath, Encoding.UTF8).Trim();
                    if (IsValidGuid(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to read device.id file: {ex.Message}");
            }
            return null;
        }

        private static string ReadFromSettingsJson()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                    var obj = JObject.Parse(json);
                    var value = obj[RegistryValueName]?.ToString();
                    if (IsValidGuid(value))
                        return value;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to read device ID from settings.json: {ex.Message}");
            }
            return null;
        }

        private static void WriteToRegistry(string deviceId)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, true))
                {
                    key?.SetValue(RegistryValueName, deviceId, RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Registry not writable, using device.id file only: {ex.Message}");
            }
        }

        private static void WriteToDeviceIdFile(string deviceId)
        {
            try
            {
                var dir = Path.GetDirectoryName(_deviceIdFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_deviceIdFilePath, deviceId, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to write device.id file: {ex.Message}");
            }
        }

        private static void WriteToSettingsJson(string deviceId)
        {
            try
            {
                JObject obj;
                if (File.Exists(_settingsFilePath))
                    obj = JObject.Parse(File.ReadAllText(_settingsFilePath, Encoding.UTF8));
                else
                    obj = new JObject();

                obj[RegistryValueName] = deviceId;

                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_settingsFilePath, obj.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Failed to write device ID to settings.json: {ex.Message}");
            }
        }

        private static bool IsValidGuid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out _);
        }
    }
}
