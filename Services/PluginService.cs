using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluxDB.Models;
using FluxDB.Plugin;

namespace FluxDB.Services
{
    public enum PluginStatus
    {
        Loaded,
        Failed,
        Disabled
    }

    public class PluginInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public PluginStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public IFluxDBPlugin Instance { get; set; }
        public PluginContext Context { get; set; }
    }

    public static class PluginService
    {
        private static readonly object _lock = new object();
        private static readonly List<PluginInfo> _plugins = new List<PluginInfo>();
        private static DatabaseService _database;
        private static ExportService _export;
        private static SettingsService _settings;
        private static bool _initialized;

        public static IReadOnlyList<PluginInfo> Plugins
        {
            get { lock (_lock) return _plugins.ToList(); }
        }

        public static void Initialize(DatabaseService database, ExportService export, SettingsService settings)
        {
            if (_initialized) return;
            _initialized = true;

            _database = database;
            _export = export;
            _settings = settings;

            var pluginDirs = GetPluginDirectories();
            if (pluginDirs.Count == 0)
            {
                LoggingService.Log("[PluginService] No plugin directories found");
                return;
            }

            var settingsObj = settings.Load();
            var disabledPlugins = settingsObj.DisabledPlugins ?? new List<string>();

            foreach (var pluginDir in pluginDirs)
            {
                if (!Directory.Exists(pluginDir))
                {
                    try { Directory.CreateDirectory(pluginDir); } catch { }
                    continue;
                }

                string[] dllFiles;
                try
                {
                    dllFiles = Directory.GetFiles(pluginDir, "*.dll");
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"[PluginService] Failed to read plugin directory '{pluginDir}': {ex.Message}");
                    continue;
                }

                foreach (var dllPath in dllFiles)
                {
                    LoadPlugin(dllPath, disabledPlugins);
                }
            }

            LoggingService.Log($"[PluginService] Loaded {_plugins.Count(p => p.Status == PluginStatus.Loaded)} plugins");
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                foreach (var plugin in _plugins)
                {
                    if (plugin.Instance != null)
                    {
                        try { plugin.Instance.Shutdown(); }
                        catch (Exception ex)
                        {
                            LoggingService.Log($"[PluginService] Error shutting down plugin '{plugin.Name}': {ex.Message}");
                        }
                    }
                }
                _plugins.Clear();
            }
            _initialized = false;
        }

        public static List<(string Header, Action Callback)> GetMenuItems()
        {
            lock (_lock)
            {
                return _plugins
                    .Where(p => p.Status == PluginStatus.Loaded && p.Context != null)
                    .SelectMany(p => p.Context.MenuItems)
                    .ToList();
            }
        }

        public static List<(string Header, Action<FileEntry> Callback)> GetContextMenuItems()
        {
            lock (_lock)
            {
                return _plugins
                    .Where(p => p.Status == PluginStatus.Loaded && p.Context != null)
                    .SelectMany(p => p.Context.ContextMenuItems)
                    .ToList();
            }
        }

        public static List<(string Label, string Icon, Action Callback)> GetToolbarButtons()
        {
            lock (_lock)
            {
                return _plugins
                    .Where(p => p.Status == PluginStatus.Loaded && p.Context != null)
                    .SelectMany(p => p.Context.ToolbarButtons)
                    .ToList();
            }
        }

        public static void RaiseFileIndexed(FileEntry file, string folderPath)
        {
            var args = new FileEventArgs(file, folderPath);
            List<PluginInfo> plugins;
            lock (_lock) plugins = _plugins.Where(p => p.Status == PluginStatus.Loaded && p.Context != null).ToList();
            foreach (var plugin in plugins)
            {
                plugin.Context.RaiseFileIndexed(args);
            }
        }

        public static void RaiseFileSelected(FileEntry file, string folderPath)
        {
            var args = new FileEventArgs(file, folderPath);
            List<PluginInfo> plugins;
            lock (_lock) plugins = _plugins.Where(p => p.Status == PluginStatus.Loaded && p.Context != null).ToList();
            foreach (var plugin in plugins)
            {
                plugin.Context.RaiseFileSelected(args);
            }
        }

        public static void RaiseSearchPerformed(string query, List<FileEntry> results, string folderPath)
        {
            var args = new SearchEventArgs(query, results, folderPath);
            List<PluginInfo> plugins;
            lock (_lock) plugins = _plugins.Where(p => p.Status == PluginStatus.Loaded && p.Context != null).ToList();
            foreach (var plugin in plugins)
            {
                plugin.Context.RaiseSearchPerformed(args);
            }
        }

        public static void SetCurrentRootFolder(string folderPath)
        {
            lock (_lock)
            {
                foreach (var plugin in _plugins)
                {
                    if (plugin.Context != null)
                        plugin.Context.CurrentRootFolder = folderPath;
                }
            }
        }

        private static void LoadPlugin(string dllPath, List<string> disabledPlugins)
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var pluginName = assemblyName.Name;

                if (disabledPlugins.Contains(pluginName, StringComparer.OrdinalIgnoreCase))
                {
                    LoggingService.Log($"[PluginService] Skipping disabled plugin: {pluginName}");
                    return;
                }

                lock (_lock)
                {
                    if (_plugins.Any(p => string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase)))
                    {
                        LoggingService.Log($"[PluginService] Plugin '{pluginName}' already loaded, skipping duplicate");
                        return;
                    }
                }

                var assembly = Assembly.LoadFrom(dllPath);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IFluxDBPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();

                foreach (var pluginType in pluginTypes)
                {
                    try
                    {
                        var instance = (IFluxDBPlugin)Activator.CreateInstance(pluginType);
                        var context = new PluginContext(_database, _export, _settings);

                        instance.Initialize(context);

                        var info = new PluginInfo
                        {
                            Name = instance.Name ?? pluginName,
                            Version = instance.Version ?? "1.0",
                            Author = instance.Author ?? "",
                            Description = instance.Description ?? "",
                            Status = PluginStatus.Loaded,
                            Instance = instance,
                            Context = context
                        };

                        lock (_lock) _plugins.Add(info);
                        LoggingService.Log($"[PluginService] Loaded plugin: {info.Name} v{info.Version} by {info.Author}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log($"[PluginService] Failed to initialize plugin type '{pluginType.FullName}': {ex.Message}");
                        lock (_lock)
                        {
                            _plugins.Add(new PluginInfo
                            {
                                Name = pluginType.FullName,
                                Status = PluginStatus.Failed,
                                ErrorMessage = ex.Message
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"[PluginService] Failed to load assembly '{dllPath}': {ex.Message}");
            }
        }

        private static List<string> GetPluginDirectories()
        {
            var dirs = new List<string>();

            try
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FluxDB", "Plugins");
                dirs.Add(appData);
            }
            catch { }

            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var localPlugins = Path.Combine(exeDir, "Plugins");
                    dirs.Add(localPlugins);
                }
            }
            catch { }

            return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}