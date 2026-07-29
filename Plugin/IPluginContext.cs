using System;
using FluxDB.Models;
using FluxDB.Services;

namespace FluxDB.Plugin
{
    public interface IPluginContext
    {
        DatabaseService Database { get; }
        ExportService Export { get; }
        SettingsService Settings { get; }

        string CurrentRootFolder { get; }
        void Log(string message);

        void RegisterMenuItem(string header, Action callback);
        void RegisterContextMenuItem(string header, Action<FileEntry> callback);
        void RegisterToolbarButton(string label, string icon, Action callback);

        event EventHandler<FileEventArgs> FileIndexed;
        event EventHandler<FileEventArgs> FileSelected;
        event EventHandler<SearchEventArgs> SearchPerformed;
    }
}