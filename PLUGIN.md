# FluxDB Plugin Development Guide

FluxDB supports a plugin system that lets you extend its functionality with custom logic, UI integrations, and automated workflows.

---

## Quick Start

1. Copy the `FluxDB-Plugin-Template/` folder to your workspace
2. Edit `MyPlugin.cs` to implement your logic
3. Build: `msbuild PluginTemplate.csproj /p:Configuration=Release`
4. Copy the output DLL to `%LocalAppData%\FluxDB\Plugins\` (or `{FluxDB.exe directory}\Plugins\`)
5. Restart FluxDB — your plugin loads automatically

---

## Plugin Interface

Every plugin must implement the `IFluxDBPlugin` interface:

```csharp
public interface IFluxDBPlugin
{
    string Name { get; }          // Display name of your plugin
    string Version { get; }       // Semantic version (e.g., "1.0.0")
    string Author { get; }        // Your name or organization
    string Description { get; }   // Short description shown in the Plugins tab
    void Initialize(IPluginContext context);
    void Shutdown();
}
```

### `Initialize(IPluginContext context)`

Called when FluxDB loads your plugin. This is where you register event handlers, menu items, and perform setup. The `context` object gives you access to all FluxDB services.

### `Shutdown()`

Called when FluxDB closes. Clean up any resources, unsubscribe from events, and save state.

---

## Plugin Context

The `IPluginContext` interface provides access to FluxDB internals:

```csharp
public interface IPluginContext
{
    DatabaseService Database { get; }    // Full SQLite database access
    ExportService Export { get; }        // JSON/GZip export
    SettingsService Settings { get; }    // App settings read/write

    string CurrentRootFolder { get; }    // Currently indexed root folder path

    void Log(string message);            // Writes to FluxDB's log file

    void RegisterMenuItem(string header, Action callback);
    void RegisterContextMenuItem(string header, Action<FileEntry> callback);
    void RegisterToolbarButton(string label, string icon, Action callback);

    event EventHandler<FileEventArgs> FileIndexed;
    event EventHandler<FileEventArgs> FileSelected;
    event EventHandler<SearchEventArgs> SearchPerformed;
}
```

### DatabaseService

The `DatabaseService` gives you full read/write access to the SQLite database. Key methods:

| Method | Description |
|---|---|
| `GetAllFiles()` | Returns all indexed files |
| `SearchFiles(string query)` | Full-text search across file names, paths, and tags |
| `SearchFiles(string query, string folderPath)` | Search limited to a folder |
| `GetTagsForFile(int fileId)` | Returns list of tag names for a file |
| `GetFileTags(int fileId)` | Alias for `GetTagsForFile` |
| `AddTagToFile(int fileId, string tagName)` | Adds a single tag to a file |
| `SetTagsForFile(int fileId, List<string> tags)` | Replaces all tags for a file |
| `GetAllTags()` | Returns all tags in the database |
| `GetNoteForFile(int fileId)` | Returns the note for a file |
| `SetNoteForFile(int fileId, string note)` | Sets the note for a file |
| `UpsertFile(FileEntry, transaction)` | Inserts or updates a file entry |
| `MarkFileAsDeleted(int fileId)` | Marks a file as deleted |
| `MarkPathAsDeleted(string path)` | Marks all files under a path as deleted |
| `UpdateFilePathAndName(int id, string path, string name)` | Updates after rename |
| `UpdateFolderPath(string oldPath, string newPath)` | Updates after folder rename |

**Important**: All database operations are synchronous. SQLite writes are not thread-safe — all DB access is serialized through a single connection. Do not call DB methods from background threads without proper synchronization.

### ExportService

Use the `ExportService` to create JSON or GZip exports of the current index:

```csharp
var export = context.Export.CreateExport(context.CurrentRootFolder);
// export.Files contains all FileEntry objects with tags and notes
```

### SettingsService

Read and write application settings:

```csharp
var settings = context.Settings.Load();
// Modify settings.SomeProperty
context.Settings.Save(settings);
```

---

## Events

### FileIndexed

Fires every time a file is indexed during scanning. Use this for auto-tagging, analytics, or custom processing.

```csharp
context.FileIndexed += (sender, args) =>
{
    var file = args.File;          // FileEntry
    var folderPath = args.FolderPath;

    // Example: auto-tag PDFs
    if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        context.Database.AddTagToFile(file.Id, "document");
    }
};
```

**Caution**: This event fires potentially thousands of times during a scan. Keep handlers fast and avoid UI operations. Use `Dispatcher.BeginInvoke` if you need to update the UI.

### FileSelected

Fires when the user selects a file in the file list:

```csharp
context.FileSelected += (sender, args) =>
{
    var file = args.File;
    context.Log($"User selected: {file.Name}");
};
```

### SearchPerformed

Fires after a search completes:

```csharp
context.SearchPerformed += (sender, args) =>
{
    context.Log($"Search '{args.Query}' returned {args.Results.Count} results");
};
```

---

## UI Integration

### Menu Items

Register items in the main menu bar:

```csharp
context.RegisterMenuItem("Plugin: Show Report", () =>
{
    MessageBox.Show("Report generated!", "My Plugin");
});
```

### Context Menu Items

Register items in the right-click context menu of the file list:

```csharp
context.RegisterContextMenuItem("Plugin: Analyze", (file) =>
{
    var tags = context.Database.GetTagsForFile(file.Id);
    MessageBox.Show($"{file.Name} has {tags.Count} tags", "Analyze");
});
```

The callback receives the `FileEntry` that was right-clicked.

### Toolbar Buttons

Register buttons in the toolbar area:

```csharp
context.RegisterToolbarButton("Stats", "\uE9D2", () =>
{
    // Show statistics
});
```

The `icon` parameter accepts Segoe MDL2 Assets Unicode characters.

---

## Database Access Examples

### Finding all files with a specific extension

```csharp
var allFiles = context.Database.GetAllFiles();
var pdfs = allFiles.Where(f =>
    f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
).ToList();
```

### Counting files by tag

```csharp
var allTags = context.Database.GetAllTags();
foreach (var tag in allTags)
{
    var files = context.Database.SearchByTag(tag.Name);
    context.Log($"Tag '{tag.Name}': {files.Count} files");
}
```

### Adding a note to a file

```csharp
context.Database.SetNoteForFile(fileId, "Reviewed on 2025-01-15");
```

---

## Complete Plugin Examples

### Example 1: AutoTagger

Automatically tags files based on extension:

```csharp
using System;
using System.Collections.Generic;
using FluxDB.Plugin;
using FluxDB.Models;

namespace FluxDB.Plugins
{
    public class AutoTagger : IFluxDBPlugin
    {
        private IPluginContext _context;
        private static readonly Dictionary<string, string> ExtensionTags = new Dictionary<string, string>
        {
            { ".pdf", "document" },
            { ".doc", "document" },
            { ".docx", "document" },
            { ".xls", "spreadsheet" },
            { ".xlsx", "spreadsheet" },
            { ".jpg", "image" },
            { ".png", "image" },
            { ".mp4", "video" },
            { ".mp3", "audio" },
            { ".cs", "code" },
            { ".json", "code" },
            { ".xml", "code" },
        };

        public string Name => "AutoTagger";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "Automatically tags files based on their extension.";

        public void Initialize(IPluginContext context)
        {
            _context = context;
            _context.FileIndexed += OnFileIndexed;
            _context.Log("AutoTagger initialized.");
        }

        public void Shutdown()
        {
            if (_context != null) _context.FileIndexed -= OnFileIndexed;
        }

        private void OnFileIndexed(object sender, FileEventArgs e)
        {
            var ext = (e.File.Extension ?? "").ToLower();
            if (ExtensionTags.TryGetValue(ext, out var tag))
            {
                try
                {
                    _context.Database.AddTagToFile(e.File.Id, tag);
                }
                catch (Exception ex)
                {
                    _context.Log($"AutoTagger error: {ex.Message}");
                }
            }
        }
    }
}
```

### Example 2: FileStats

Adds a menu item that shows statistics about the current index:

```csharp
using System;
using System.Linq;
using System.Windows;
using FluxDB.Plugin;
using FluxDB.Models;

namespace FluxDB.Plugins
{
    public class FileStats : IFluxDBPlugin
    {
        private IPluginContext _context;

        public string Name => "FileStats";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "Shows file statistics for the current index.";

        public void Initialize(IPluginContext context)
        {
            _context = context;
            _context.RegisterMenuItem("Plugin: File Statistics", ShowStats);
        }

        public void Shutdown() { }

        private void ShowStats()
        {
            var files = _context.Database.GetAllFiles();
            var totalSize = files.Sum(f => f.Size);
            var byExtension = files
                .GroupBy(f => (f.Extension ?? "").ToLower())
                .OrderByDescending(g => g.Count())
                .Take(10);

            var stats = $"Total files: {files.Count}\n";
            stats += $"Total size: {FormatSize(totalSize)}\n\n";
            stats += "Top 10 extensions:\n";

            foreach (var group in byExtension)
            {
                stats += $"  {group.Key}: {group.Count()} files\n";
            }

            MessageBox.Show(stats, "File Statistics", MessageBoxButton.OK, MessageBoxImage.Information);
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
```

### Example 3: Custom CSV Exporter

Adds a context menu item to export selected file details as CSV:

```csharp
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Linq;
using FluxDB.Plugin;
using FluxDB.Models;

namespace FluxDB.Plugins
{
    public class CsvExporter : IFluxDBPlugin
    {
        private IPluginContext _context;

        public string Name => "CSV Exporter";
        public string Version => "1.0.0";
        public string Author => "Your Name";
        public string Description => "Exports file list as CSV.";

        public void Initialize(IPluginContext context)
        {
            _context = context;
            _context.RegisterContextMenuItem("Export as CSV", ExportToCsv);
        }

        public void Shutdown() { }

        private void ExportToCsv(FileEntry file)
        {
            try
            {
                var allFiles = _context.Database.GetAllFiles();
                var sb = new StringBuilder();
                sb.AppendLine("Path,Name,Extension,Size,Created,Modified,Tags");

                foreach (var f in allFiles)
                {
                    var tags = string.Join(";", f.Tags ?? new System.Collections.Generic.List<string>());
                    sb.AppendLine($"\"{f.Path}\",\"{f.Name}\",\"{f.Extension}\",{f.Size},{f.CreatedAt:o},{f.ModifiedAt:o},\"{tags}\"");
                }

                var exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"fluxdb-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
                );
                File.WriteAllText(exportPath, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Exported {allFiles.Count} files to:\n{exportPath}",
                    "CSV Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _context.Log($"CSV export error: {ex.Message}");
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
```

---

## Error Handling

- Always wrap your code in try/catch blocks. An unhandled exception in a plugin crashes the plugin but does not crash FluxDB.
- Use `context.Log()` for error logging — this writes to FluxDB's log file (`%LocalAppData%\FluxDB\logs.txt`).
- Never throw exceptions from event handlers. FluxDB catches them, but they will be logged as errors.
- For UI operations (MessageBox, window dialogs), use the main thread or `Application.Current.Dispatcher.BeginInvoke`.

---

## Build & Deployment

### Prerequisites

- .NET Framework 4.7.2 SDK
- Reference to `FluxDB.exe` (already configured in the template)

### Build

```bash
msbuild PluginTemplate.csproj /p:Configuration=Release
```

### Deploy

Copy the output DLL and any dependencies to the Plugins folder:

```
%LocalAppData%\FluxDB\Plugins\MyPlugin.dll
```

Or if you prefer the local directory:

```
{FluxDB.exe directory}\Plugins\MyPlugin.dll
```

FluxDB scans both directories on startup. The `%LocalAppData%` path is preferred for production.

### Enabling/Disabling

Open Settings → Plugins tab. Each plugin can be toggled on/off. Disabled plugins are persisted in `settings.json` under `DisabledPlugins` and skipped on next startup.

---

## API Reference

### `IFluxDBPlugin`

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | Display name |
| `Version` | `string` | Semantic version |
| `Author` | `string` | Author/org name |
| `Description` | `string` | Short description |
| `Initialize(IPluginContext)` | `void` | Called on load |
| `Shutdown()` | `void` | Called on unload |

### `IPluginContext`

| Member | Type | Description |
|---|---|---|
| `Database` | `DatabaseService` | Database access |
| `Export` | `ExportService` | Export service |
| `Settings` | `SettingsService` | Settings service |
| `CurrentRootFolder` | `string` | Root folder path |
| `Log(string)` | `void` | Write to log |
| `RegisterMenuItem(...)` | `void` | Add menu item |
| `RegisterContextMenuItem(...)` | `void` | Add context menu item |
| `RegisterToolbarButton(...)` | `void` | Add toolbar button |
| `FileIndexed` | `event` | File indexed |
| `FileSelected` | `event` | File selected |
| `SearchPerformed` | `event` | Search performed |

### `FileEventArgs`

| Property | Type | Description |
|---|---|---|
| `File` | `FileEntry` | The file entry |
| `FolderPath` | `string` | Root folder path |

### `SearchEventArgs`

| Property | Type | Description |
|---|---|---|
| `Query` | `string` | Search query |
| `Results` | `List<FileEntry>` | Search results |
| `FolderPath` | `string` | Root folder path |

### `FileEntry` (key properties)

| Property | Type | Description |
|---|---|---|
| `Id` | `int` | Database ID |
| `Path` | `string` | Full file path |
| `Name` | `string` | File name |
| `Extension` | `string` | File extension (with dot) |
| `Size` | `long` | File size in bytes |
| `CreatedAt` | `DateTime` | Creation date |
| `ModifiedAt` | `DateTime` | Last modified date |
| `IsFolder` | `bool` | True if this is a folder |
| `Tags` | `List<string>` | Tag list |
| `TagsText` | `string` | Null-byte separated tag string |
| `Note` | `string` | User note |

---

## FAQ

**Q: Can I add custom UI windows?**
Yes. Use `System.Windows.Window` or `MessageBox` as in the examples above. Set `window.Owner = Application.Current.MainWindow` for proper parent-child behavior.

**Q: Can I use NuGet packages in my plugin?**
Yes, but all dependencies must be included in the Plugins folder alongside your DLL. Use `CopyLocalLockFileAssemblies` in your project file.

**Q: What happens if my plugin throws an exception?**
FluxDB catches it, logs the error, and marks the plugin as Failed. Other plugins continue to run.

**Q: Can I access the file system directly?**
Yes, you can use `System.IO` to read/write files. The plugin runs with the same permissions as FluxDB.

**Q: Is there a way to persist plugin settings?**
Use `context.Settings.Load()` / `context.Settings.Save()` to store data in `settings.json`. Avoid storing large data — use the SQLite database for that.

**Q: Can I react to file deletions?**
Not directly via events. You can periodically scan with `Database.GetAllFiles()` and check for files marked as deleted (`Deleted == true`).

---

## Troubleshooting

| Problem | Solution |
|---|---|
| Plugin not loading | Check `%LocalAppData%\FluxDB\logs.txt` for errors |
| Plugin appears as "Failed" | Check the Settings → Plugins tab for the error message |
| Missing method exception | Make sure your plugin targets .NET Framework 4.7.2 |
| DLL not found in Plugins folder | The folder must be named exactly `Plugins` (capital P) |
| Database locked errors | Don't call DB methods from background threads |
| UI freezes during plugin code | Use `Dispatcher.BeginInvoke` for UI updates from events |