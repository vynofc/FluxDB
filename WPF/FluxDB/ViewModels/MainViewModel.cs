using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace FluxDB.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private DatabaseService _databaseService;
        private IndexerService _indexerService;
        private ExportService _exportService;
        private ImportService _importService;

        private CancellationTokenSource _indexCancellation;
        private CancellationTokenSource _previewCts;

        private const string DatabaseFileName = ".fluxdb";

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".svg" };
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };
        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt" };
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz" };
        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xaml", ".xml", ".json", ".sql" };
        private static readonly HashSet<string> PreviewTextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xaml", ".xml", ".json", ".sql", ".log", ".ini", ".cfg", ".bat", ".ps1", ".sh", ".yml", ".yaml", ".toml", ".config" };

        // Navigation
        public NavigationViewModel Navigation { get; }

        [ObservableProperty]
        private ObservableCollection<NavigationViewItem> _navigationItems = new();

        [ObservableProperty]
        private ObservableCollection<NavigationViewItem> _footerNavigationItems = new();

        // Files
        [ObservableProperty]
        private ObservableCollection<FileEntry> _files = new();

        [ObservableProperty]
        private FileEntry _selectedFile;

        [ObservableProperty]
        private bool _isDetailOpen;

        // Indexing
        [ObservableProperty]
        private bool _isIndexing;

        [ObservableProperty]
        private double _indexProgress;

        [ObservableProperty]
        private string _indexStatus = "Ready";

        [ObservableProperty]
        private string _statusMessage = "Ready - Select a folder or drag & drop to start";

        [ObservableProperty]
        private string _fileCountText = "0 items";

        // Search
        [ObservableProperty]
        private string _searchText;

        [ObservableProperty]
        private string _currentFilter = "All Files";

        // Preview
        [ObservableProperty]
        private ImageSource _previewImage;

        [ObservableProperty]
        private string _previewText;

        [ObservableProperty]
        private bool _isImagePreviewVisible;

        [ObservableProperty]
        private bool _isTextPreviewVisible;

        [ObservableProperty]
        private bool _isNoPreviewVisible;

        [ObservableProperty]
        private bool _isPdfPreviewVisible;

        [ObservableProperty]
        private bool _isPreviewPanelVisible;

        // Tags & Notes
        [ObservableProperty]
        private string _tagsText;

        [ObservableProperty]
        private string _noteText;

        // Clipboard
        private List<string> _clipboardFiles = new();
        private bool _clipboardIsCut;

        public NavigationViewModel GetNavigation() => Navigation;

        public MainViewModel(SettingsService settingsService, NavigationViewModel navigationViewModel)
        {
            _settingsService = settingsService;
            Navigation = navigationViewModel;

            NavigationItems = new ObservableCollection<NavigationViewItem>
            {
                new() { Content = "Home", Icon = new SymbolIcon(SymbolRegular.Home24), Tag = "dashboard" },
                new() { Content = "File Browser", Icon = new SymbolIcon(SymbolRegular.Folder24), Tag = "fileBrowser" },
            };

            FooterNavigationItems = new ObservableCollection<NavigationViewItem>
            {
                new() { Content = "Theme", Icon = new SymbolIcon(SymbolRegular.WeatherSunny24), Tag = "theme" },
                new() { Content = "Settings", Icon = new SymbolIcon(SymbolRegular.Settings24), Tag = "settings" },
            };

            WeakReferenceMessenger.Default.Register<FolderOpenedMessage>(this, (r, m) =>
            {
                _ = OpenFolderAsync(m.FolderPath);
            });
        }

        private void OnNavigationChanged()
        {
            _ = RefreshCurrentFolderViewAsync();
        }

        public void InitializeServices(DatabaseService db, IndexerService indexer, ExportService export, ImportService import)
        {
            _databaseService = db;
            _indexerService = indexer;
            _exportService = export;
            _importService = import;

            _indexerService.ProgressChanged += OnIndexProgress;
            _indexerService.StatusChanged += OnIndexStatus;
        }

        public async Task LoadInitialData()
        {
            var settings = _settingsService.Load();
            if (!string.IsNullOrEmpty(settings.LastRootFolder) && Directory.Exists(settings.LastRootFolder))
            {
                await OpenFolderAsync(settings.LastRootFolder);
            }
        }

        [RelayCommand]
        private async Task SelectFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await OpenFolderAsync(dialog.SelectedPath);
            }
        }

        [RelayCommand]
        private async Task OpenFolder(string folderPath)
        {
            await OpenFolderAsync(folderPath);
        }

        public async Task OpenFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            var dbPath = Path.Combine(folderPath, DatabaseFileName);
            _databaseService?.Dispose();
            if (_indexerService != null)
            {
                _indexerService.ProgressChanged -= OnIndexProgress;
                _indexerService.StatusChanged -= OnIndexStatus;
            }

            _databaseService = new DatabaseService(dbPath);
            _indexerService = new IndexerService(_databaseService);
            _exportService = new ExportService(_databaseService, _settingsService);
            _importService = new ImportService(_databaseService);
            _indexerService.ProgressChanged += OnIndexProgress;
            _indexerService.StatusChanged += OnIndexStatus;

            _settingsService.AddRecentFolder(folderPath);
            Navigation.SetRootFolder(folderPath);

            if (File.Exists(dbPath) && _databaseService.GetFileCount() > 0)
            {
                var result = System.Windows.MessageBox.Show(
                    $"This folder already has an index with {_databaseService.GetFileCount()} files.\n\n" +
                    "Do you want to refresh the index?\n\nYes = Refresh | No = Use existing",
                    "Existing Index Found", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                    await StartIndexing();
                else
                {
                    await RefreshCurrentFolderViewAsync();
                    StatusMessage = "Loaded existing index";
                }
            }
            else
            {
                await StartIndexing();
            }
        }

        [RelayCommand]
        private async Task Refresh()
        {
            if (_indexerService == null) return;
            await StartIndexing();
        }

        private async Task StartIndexing()
        {
            if (_indexerService == null || Navigation.CurrentRootFolder == null) return;

            IsIndexing = true;
            IndexProgress = 0;
            StatusMessage = "Indexing...";

            _indexCancellation?.Cancel();
            _indexCancellation?.Dispose();
            _indexCancellation = new CancellationTokenSource();

            try
            {
                var result = await _indexerService.ScanFolderAsync(Navigation.CurrentRootFolder, _indexCancellation.Token);
                if (result.Cancelled)
                    StatusMessage = "Indexing cancelled";
                else
                    StatusMessage = $"Indexing complete - {result.FilesIndexed} files indexed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Indexing error: {ex.Message}";
                LoggingService.Log($"Indexing error: {ex}");
            }
            finally
            {
                IsIndexing = false;
                await RefreshCurrentFolderViewAsync();
            }
        }

        [RelayCommand]
        private void CancelIndex()
        {
            _indexCancellation?.Cancel();
        }

        public async Task RefreshCurrentFolderViewAsync()
        {
            if (_databaseService == null || string.IsNullOrEmpty(Navigation.CurrentViewFolder))
            {
                Files = new ObservableCollection<FileEntry>();
                FileCountText = "0 items";
                return;
            }

            var currentFolder = Navigation.CurrentViewFolder;
            var items = new List<FileEntry>();

            var directories = await Task.Run(() =>
            {
                try
                {
                    return Directory.GetDirectories(currentFolder)
                        .Where(d => !Path.GetFileName(d).StartsWith("."))
                        .Where(d => (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                        .OrderBy(d => Path.GetFileName(d))
                        .ToList();
                }
                catch { return new List<string>(); }
            });

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                items.Add(new FileEntry
                {
                    Id = -1,
                    Path = dir,
                    Name = dirInfo.Name,
                    IsFolder = true,
                    CreatedAt = dirInfo.CreationTime,
                    ModifiedAt = dirInfo.LastWriteTime
                });
            }

            var allFiles = await Task.Run(() => _databaseService.GetFilesInFolder(currentFolder));
            var filesInFolder = allFiles
                .Where(f => Path.GetDirectoryName(f.Path) == currentFolder)
                .Where(f => !f.Name.StartsWith(".") && !string.Equals(f.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Where(f =>
                {
                    try { return (File.GetAttributes(f.Path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                    catch { return false; }
                })
                .Where(MatchesFilter)
                .OrderBy(f => f.Name);

            items.AddRange(filesInFolder);
            Files = new ObservableCollection<FileEntry>(items);

            var folderCount = items.Count(i => i.IsFolder);
            var fileCount = items.Count(i => !i.IsFolder);
            FileCountText = $"{folderCount} folders, {fileCount} files";
        }

        private bool MatchesFilter(FileEntry entry)
        {
            if (entry.IsFolder) return true;
            if (CurrentFilter == "All Files") return true;
            var ext = (entry.Extension ?? "").ToLower();

            return CurrentFilter switch
            {
                "Images" => ImageExtensions.Contains(ext),
                "Audio" => AudioExtensions.Contains(ext),
                "Video" => VideoExtensions.Contains(ext),
                "Documents" => DocumentExtensions.Contains(ext),
                "Archives" => ArchiveExtensions.Contains(ext),
                "Code" => CodeExtensions.Contains(ext),
                _ => true
            };
        }

        partial void OnSelectedFileChanged(FileEntry value)
        {
            if (value != null)
            {
                IsDetailOpen = true;
                TagsText = value.TagsText ?? "";
                NoteText = value.Note ?? "";
                UpdatePreview(value);
            }
            else
            {
                IsDetailOpen = false;
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                _ = RefreshCurrentFolderViewAsync();
            }
            else
            {
                Search();
            }
        }

        partial void OnCurrentFilterChanged(string value)
        {
            _ = RefreshCurrentFolderViewAsync();
        }

        [RelayCommand]
        private void Search()
        {
            if (string.IsNullOrEmpty(SearchText))
            {
                _ = RefreshCurrentFolderViewAsync();
                return;
            }

            if (_databaseService == null) return;
            var results = _databaseService.SearchFiles(SearchText, Navigation.CurrentViewFolder)
                .Where(f => !f.Name.StartsWith(".") && !string.Equals(f.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Where(f =>
                {
                    try { return (File.GetAttributes(f.Path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                    catch { return false; }
                })
                .ToList();
            Files = new ObservableCollection<FileEntry>(results);
            FileCountText = $"{results.Count} results";
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = "";
            _ = RefreshCurrentFolderViewAsync();
        }

        [RelayCommand]
        private void FilterChanged(string filter)
        {
            CurrentFilter = filter ?? "All Files";
            _ = RefreshCurrentFolderViewAsync();
        }

        [RelayCommand]
        private void NavigateToBreadcrumb(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Navigation.NavigateTo(path);
            _ = RefreshCurrentFolderViewAsync();
        }

        [RelayCommand]
        private void Copy()
        {
            if (SelectedFile == null) return;
            _clipboardFiles = new List<string> { SelectedFile.Path };
            _clipboardIsCut = false;
            StatusMessage = "Copied to clipboard";
        }

        [RelayCommand]
        private void Cut()
        {
            if (SelectedFile == null) return;
            _clipboardFiles = new List<string> { SelectedFile.Path };
            _clipboardIsCut = true;
            StatusMessage = "Cut to clipboard";
        }

        [RelayCommand]
        private async Task Paste()
        {
            if (_clipboardFiles.Count == 0 || string.IsNullOrEmpty(Navigation.CurrentViewFolder)) return;

            try
            {
                foreach (var srcPath in _clipboardFiles)
                {
                    var destPath = Path.Combine(Navigation.CurrentViewFolder, Path.GetFileName(srcPath));
                    if (_clipboardIsCut)
                    {
                        if (Directory.Exists(srcPath))
                        {
                            Directory.Move(srcPath, destPath);
                            _databaseService?.UpdateFolderPath(srcPath, destPath);
                        }
                        else if (File.Exists(srcPath))
                        {
                            destPath = GetUniqueFilePath(destPath);
                            File.Move(srcPath, destPath);
                            var allFiles = _databaseService?.GetAllFiles();
                            var existing = allFiles?.FirstOrDefault(f => f.Path == srcPath);
                            if (existing != null)
                                _databaseService?.UpdateFilePathAndName(existing.Id, destPath, Path.GetFileName(destPath));
                        }
                    }
                    else
                    {
                        destPath = GetUniqueFilePath(destPath);
                        if (Directory.Exists(srcPath))
                        {
                            CopyDirectoryRecursive(srcPath, destPath);
                        }
                        else if (File.Exists(srcPath))
                        {
                            File.Copy(srcPath, destPath);
                        }
                    }
                }

                if (_clipboardIsCut)
                {
                    _clipboardFiles.Clear();
                    _clipboardIsCut = false;
                }

                await RefreshCurrentFolderViewAsync();
                StatusMessage = _clipboardIsCut ? "Moved" : "Copied";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        [RelayCommand]
        private void OpenItem(FileEntry item)
        {
            if (item == null) return;
            if (item.IsFolder)
            {
                Navigation.NavigateTo(item.Path);
                _ = RefreshCurrentFolderViewAsync();
            }
            else
            {
                try
                {
                    using (var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = item.Path,
                        UseShellExecute = true
                    })) { }
                }
                catch (Exception ex)
                    {
                    System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
            }
        }

        [RelayCommand]
        private void OpenFileLocation()
        {
            if (SelectedFile == null) return;
            var info = new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedFile.Path}\"");
            using (var proc = Process.Start(info)) { }
        }

        [RelayCommand]
        private async Task SaveTags()
        {
            if (SelectedFile == null || _databaseService == null) return;
            var tags = TagsText?.Split(new[] { ',', ';', ' ', '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>();

            await Task.Run(() => _databaseService.SetTagsForFile(SelectedFile.Id, tags));
            SelectedFile.TagsText = string.Join(", ", tags);
            SelectedFile.Tags = tags;

            if (!string.IsNullOrEmpty(NoteText))
            {
                await Task.Run(() => _databaseService.SetNoteForFile(SelectedFile.Id, NoteText));
                SelectedFile.Note = NoteText;
            }

            StatusMessage = "Tags and notes saved";
        }

        [RelayCommand]
        private async Task Rename()
        {
            if (SelectedFile == null) return;

            var currentName = SelectedFile.IsFolder ? SelectedFile.Name : Path.GetFileNameWithoutExtension(SelectedFile.Name);
            var extension = SelectedFile.IsFolder ? "" : Path.GetExtension(SelectedFile.Name);

            var window = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is MainWindow);
            if (window == null) return;

            var dialog = new RenameDialog(currentName);
            dialog.Owner = window;
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var newName = dialog.NewName + extension;
                var newPath = Path.Combine(Path.GetDirectoryName(SelectedFile.Path), newName);

                try
                {
                    if (SelectedFile.IsFolder)
                    {
                        Directory.Move(SelectedFile.Path, newPath);
                        _databaseService?.UpdateFolderPath(SelectedFile.Path, newPath);
                    }
                    else
                    {
                        File.Move(SelectedFile.Path, newPath);
                        _databaseService?.UpdateFilePathAndName(SelectedFile.Id, newPath, newName);
                    }

                    await RefreshCurrentFolderViewAsync();
                    StatusMessage = $"Renamed to {newName}";
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedFile == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{SelectedFile.Name}'?\n\nThis action cannot be undone!",
                "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                if (SelectedFile.IsFolder && Directory.Exists(SelectedFile.Path))
                {
                    _databaseService?.MarkPathAsDeleted(SelectedFile.Path);
                    Directory.Delete(SelectedFile.Path, true);
                }
                else if (File.Exists(SelectedFile.Path))
                {
                    File.Delete(SelectedFile.Path);
                    _databaseService?.MarkFileAsDeleted(SelectedFile.Id);
                }

                await RefreshCurrentFolderViewAsync();
                StatusMessage = $"Deleted {SelectedFile.Name}";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not delete: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task NewFolder()
        {
            if (string.IsNullOrEmpty(Navigation.CurrentViewFolder)) return;

            var window = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is MainWindow);
            if (window == null) return;

            var dialog = new RenameDialog("New Folder");
            dialog.Owner = window;
            dialog.Title = "New Folder";
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var newPath = Path.Combine(Navigation.CurrentViewFolder, dialog.NewName);
                newPath = GetUniqueFolderPath(newPath);
                Directory.CreateDirectory(newPath);
                await RefreshCurrentFolderViewAsync();
                StatusMessage = $"Created folder: {Path.GetFileName(newPath)}";
            }
        }

        #region Preview

        private async void UpdatePreview(FileEntry file)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var ct = _previewCts.Token;

            IsImagePreviewVisible = false;
            IsTextPreviewVisible = false;
            IsNoPreviewVisible = false;
            IsPdfPreviewVisible = false;
            IsPreviewPanelVisible = false;
            PreviewImage = null;
            PreviewText = null;

            if (file == null || file.IsFolder || !File.Exists(file.Path))
                return;

            IsPreviewPanelVisible = true;
            var ext = (file.Extension ?? "").ToLower();

            if (ImageExtensions.Contains(ext))
            {
                try
                {
                    var path = file.Path;
                    var bitmap = await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 400;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }, ct);

                    PreviewImage = bitmap;
                    IsImagePreviewVisible = true;
                }
                catch (OperationCanceledException) { }
                catch
                {
                    PreviewText = "Cannot load image";
                    IsNoPreviewVisible = true;
                }
            }
            else if (PreviewTextExtensions.Contains(ext))
            {
                var path = file.Path;
                string content = null;
                try
                {
                    content = await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var reader = new StreamReader(fs, System.Text.Encoding.UTF8, true))
                            {
                                var text = reader.ReadToEnd();
                                if (text.Length > 5000)
                                    text = text.Substring(0, 5000) + "\n\n... (truncated)";
                                return text;
                            }
                        }
                        catch { return null; }
                    }, ct);
                }
                catch (OperationCanceledException) { return; }

                if (content != null)
                {
                    PreviewText = content;
                    IsTextPreviewVisible = true;
                }
                else
                {
                    PreviewText = "Cannot read file";
                    IsNoPreviewVisible = true;
                }
            }
            else
            {
                IsNoPreviewVisible = true;
            }
        }

        #endregion

        #region Index Progress Handlers

        private void OnIndexProgress(object sender, IndexProgressEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                IndexProgress = e.Percentage;
                StatusMessage = $"Indexing: {e.Current}/{e.Total} files";
            }));
        }

        private void OnIndexStatus(object sender, string status)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusMessage = status;
            }));
        }

        #endregion

        #region Helpers

        private string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path)) return path;
            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var counter = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(directory, $"{fileName} ({counter}){extension}");
                counter++;
            }
            return path;
        }

        private string GetUniqueFolderPath(string path)
        {
            if (!Directory.Exists(path)) return path;
            var parent = Path.GetDirectoryName(path);
            var folderName = Path.GetFileName(path);
            var counter = 1;
            while (Directory.Exists(path))
            {
                path = Path.Combine(parent, $"{folderName} ({counter})");
                counter++;
            }
            return path;
        }

        #endregion
    }
}