using System;
using System.Collections.Generic;
using FluxDB.Models;
using FluxDB.Services;

namespace FluxDB.Plugin
{
    public class PluginContext : IPluginContext
    {
        public DatabaseService Database { get; }
        public ExportService Export { get; }
        public SettingsService Settings { get; }

        public string CurrentRootFolder { get; set; }

        public List<(string Header, Action Callback)> MenuItems { get; } = new List<(string, Action)>();
        public List<(string Header, Action<FileEntry> Callback)> ContextMenuItems { get; } = new List<(string, Action<FileEntry>)>();
        public List<(string Label, string Icon, Action Callback)> ToolbarButtons { get; } = new List<(string, string, Action)>();

        public event EventHandler<FileEventArgs> FileIndexed;
        public event EventHandler<FileEventArgs> FileSelected;
        public event EventHandler<SearchEventArgs> SearchPerformed;

        public PluginContext(DatabaseService database, ExportService export, SettingsService settings)
        {
            Database = database;
            Export = export;
            Settings = settings;
        }

        public void Log(string message)
        {
            LoggingService.Log($"[Plugin] {message}");
        }

        public void RegisterMenuItem(string header, Action callback)
        {
            if (string.IsNullOrEmpty(header)) return;
            MenuItems.Add((header, callback));
        }

        public void RegisterContextMenuItem(string header, Action<FileEntry> callback)
        {
            if (string.IsNullOrEmpty(header)) return;
            ContextMenuItems.Add((header, callback));
        }

        public void RegisterToolbarButton(string label, string icon, Action callback)
        {
            if (string.IsNullOrEmpty(label)) return;
            ToolbarButtons.Add((label, icon, callback));
        }

        public void RaiseFileIndexed(FileEventArgs args)
        {
            try { FileIndexed?.Invoke(this, args); }
            catch (Exception ex) { Log($"Error in FileIndexed event: {ex.Message}"); }
        }

        public void RaiseFileSelected(FileEventArgs args)
        {
            try { FileSelected?.Invoke(this, args); }
            catch (Exception ex) { Log($"Error in FileSelected event: {ex.Message}"); }
        }

        public void RaiseSearchPerformed(SearchEventArgs args)
        {
            try { SearchPerformed?.Invoke(this, args); }
            catch (Exception ex) { Log($"Error in SearchPerformed event: {ex.Message}"); }
        }
    }
}