using System;
using System.Linq;
using System.Windows;
using FluxDB.Plugin;
using FluxDB.Models;
using FluxDB.Services;

namespace FluxDB.Plugins
{
    public class MyPlugin : IFluxDBPlugin
    {
        private IPluginContext _context;
        private int _indexedCount;
        private long _totalSize;

        public string Name => "My Plugin";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "Example plugin that auto-tags large files and shows statistics.";

        public void Initialize(IPluginContext context)
        {
            _context = context;
            _context.Log($"Plugin '{Name}' v{Version} initialized.");

            _context.FileIndexed += OnFileIndexed;

            _context.RegisterMenuItem("Plugin: Show Stats", ShowStats);
        }

        public void Shutdown()
        {
            if (_context != null)
            {
                _context.FileIndexed -= OnFileIndexed;
            }
            _context?.Log($"Plugin '{Name}' shutting down.");
        }

        private void OnFileIndexed(object sender, FileEventArgs e)
        {
            if (e.File == null) return;

            _indexedCount++;
            _totalSize += e.File.Size;

            if (e.File.Size > 100 * 1024 * 1024)
            {
                try
                {
                    var db = _context.Database;
                    var existingTags = db.GetFileTags(e.File.Id);
                    if (!existingTags.Any(t => string.Equals(t, "large", StringComparison.OrdinalIgnoreCase)))
                    {
                        db.AddTagToFile(e.File.Id, "large");
                    }
                }
                catch (Exception ex)
                {
                    _context.Log($"Error tagging file: {ex.Message}");
                }
            }
        }

        private void ShowStats()
        {
            var message = $"Files indexed: {_indexedCount}\n" +
                          $"Total size: {FormatSize(_totalSize)}\n" +
                          $"Current folder: {_context.CurrentRootFolder}";

            MessageBox.Show(message, "Plugin Statistics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }
    }
}