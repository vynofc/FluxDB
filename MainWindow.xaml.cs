using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;
using Microsoft.Win32;

namespace FluxDB
{
    public partial class MainWindow : Window
    {
        private SettingsService _settingsService;
        private DatabaseService _databaseService;
        private IndexerService _indexerService;
        private LicenseService _licenseService;
        private ExportService _exportService;
        
        private CancellationTokenSource _indexCancellation;
        private FileEntry _selectedFile;
        private string _currentRootFolder;
        private string _currentViewFolder;
        private bool _isIndexing;
        private bool _isSearchMode;

        // Navigation History
        private Stack<string> _backHistory = new Stack<string>();
        private Stack<string> _forwardHistory = new Stack<string>();

        // Drag & Drop
        private Point _dragStartPoint;
        private bool _isDragging;

        private const string DatabaseFileName = ".fluxdb";

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
            LoadInitialData();
        }

        private void InitializeServices()
        {
            _settingsService = new SettingsService();
            _licenseService = new LicenseService(_settingsService);
            _databaseService = null;
            _indexerService = null;
            _exportService = null;
        }

        private void InitializeDatabaseForFolder(string folderPath)
        {
            _databaseService?.Dispose();
            var dbPath = Path.Combine(folderPath, DatabaseFileName);
            _databaseService = new DatabaseService(dbPath);
            _indexerService = new IndexerService(_databaseService);
            _exportService = new ExportService(_databaseService, _settingsService);
            _indexerService.ProgressChanged += IndexerService_ProgressChanged;
            _indexerService.StatusChanged += IndexerService_StatusChanged;
        }

        private bool HasExistingIndex(string folderPath)
        {
            var dbPath = Path.Combine(folderPath, DatabaseFileName);
            return File.Exists(dbPath);
        }

        private void LoadInitialData()
        {
            var settings = _settingsService.Load();
            
            if (!string.IsNullOrEmpty(settings.LastRootFolder) && Directory.Exists(settings.LastRootFolder))
            {
                _currentRootFolder = settings.LastRootFolder;
                _currentViewFolder = _currentRootFolder;
                txtCurrentFolder.Text = $"📁 {_currentRootFolder}";
                btnRefresh.IsEnabled = true;
                btnExport.IsEnabled = true;

                InitializeDatabaseForFolder(_currentRootFolder);
                NavigateToFolder(_currentRootFolder, addToHistory: false);
                
                if (HasExistingIndex(_currentRootFolder))
                {
                    txtStatus.Text = "Index loaded from folder";
                }
            }
            else
            {
                txtStatus.Text = "Ready - Select a folder or drag & drop to start";
            }

            UpdateLicenseStatus();
        }

        #region Drag & Drop

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var firstPath = files[0];
                    
                    // Prüfen ob es ein Ordner ist (zum Öffnen als Root)
                    if (Directory.Exists(firstPath) && string.IsNullOrEmpty(_currentRootFolder))
                    {
                        txtDropHint.Text = "📂 Drop folder here to index";
                        e.Effects = DragDropEffects.Copy;
                    }
                    // Dateien/Ordner in aktuellen Ordner kopieren/verschieben
                    else if (!string.IsNullOrEmpty(_currentViewFolder))
                    {
                        var fileCount = files.Count(f => File.Exists(f));
                        var folderCount = files.Count(f => Directory.Exists(f));
                        
                        var itemText = "";
                        if (folderCount > 0 && fileCount > 0)
                            itemText = $"{folderCount} folder(s) and {fileCount} file(s)";
                        else if (folderCount > 0)
                            itemText = $"{folderCount} folder(s)";
                        else
                            itemText = $"{fileCount} file(s)";

                        // Shift = Move, sonst Copy
                        if ((e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey)
                        {
                            txtDropHint.Text = $"📥 Move {itemText} here";
                            e.Effects = DragDropEffects.Move;
                        }
                        else
                        {
                            txtDropHint.Text = $"📋 Copy {itemText} here";
                            e.Effects = DragDropEffects.Copy;
                        }
                    }
                    else
                    {
                        txtDropHint.Text = "📂 Drop a folder to start";
                        e.Effects = DragDropEffects.Copy;
                    }
                    
                    dropOverlay.Visibility = Visibility.Visible;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            dropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
                return;

            var firstPath = paths[0];
            var isMove = (e.KeyStates & DragDropKeyStates.ShiftKey) == DragDropKeyStates.ShiftKey;

            // Wenn kein Root-Ordner gesetzt ist und ein Ordner gedroppt wird -> Als Root öffnen
            if (string.IsNullOrEmpty(_currentRootFolder) && Directory.Exists(firstPath))
            {
                await OpenFolderAsync(firstPath);
                return;
            }

            // Dateien/Ordner in den aktuellen Ordner kopieren/verschieben
            if (!string.IsNullOrEmpty(_currentViewFolder))
            {
                await CopyOrMoveFilesAsync(paths, _currentViewFolder, isMove);
            }
        }

        private async Task CopyOrMoveFilesAsync(string[] sourcePaths, string targetFolder, bool move)
        {
            var copiedCount = 0;
            var movedCount = 0;
            var errorCount = 0;
            var skippedCount = 0;

            await Task.Run(() =>
            {
                foreach (var sourcePath in sourcePaths)
                {
                    try
                    {
                        // Prüfen ob Quelle = Ziel
                        var sourceParent = Path.GetDirectoryName(sourcePath);
                        if (sourceParent.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            skippedCount++;
                            continue;
                        }

                        if (File.Exists(sourcePath))
                        {
                            // Datei kopieren/verschieben
                            var fileName = Path.GetFileName(sourcePath);
                            var targetPath = Path.Combine(targetFolder, fileName);
                            targetPath = GetUniqueFilePath(targetPath);

                            if (move)
                            {
                                File.Move(sourcePath, targetPath);
                                movedCount++;
                            }
                            else
                            {
                                File.Copy(sourcePath, targetPath);
                                copiedCount++;
                            }

                            // Zur Datenbank hinzufügen
                            Dispatcher.Invoke(() => AddFileToIndex(targetPath));
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            // Ordner kopieren/verschieben
                            var dirName = Path.GetFileName(sourcePath);
                            var targetPath = Path.Combine(targetFolder, dirName);
                            targetPath = GetUniqueFolderPath(targetPath);

                            if (move)
                            {
                                Directory.Move(sourcePath, targetPath);
                                movedCount++;
                            }
                            else
                            {
                                CopyDirectory(sourcePath, targetPath);
                                copiedCount++;
                            }

                            // Alle Dateien im Ordner zur Datenbank hinzufügen
                            Dispatcher.Invoke(() => AddFolderToIndex(targetPath));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error copying/moving {sourcePath}: {ex.Message}");
                        errorCount++;
                    }
                }
            });

            // UI aktualisieren
            RefreshCurrentFolderView();

            // Status-Meldung
            var actionText = move ? "Moved" : "Copied";
            var totalCount = move ? movedCount : copiedCount;
            
            if (errorCount > 0)
            {
                txtStatus.Text = $"{actionText} {totalCount} item(s), {errorCount} error(s)";
            }
            else if (skippedCount > 0)
            {
                txtStatus.Text = $"{actionText} {totalCount} item(s), {skippedCount} skipped (same location)";
            }
            else
            {
                txtStatus.Text = $"{actionText} {totalCount} item(s) successfully";
            }
        }

        private void AddFileToIndex(string filePath)
        {
            if (_databaseService == null) return;

            try
            {
                var fileInfo = new FileInfo(filePath);
                var entry = new FileEntry
                {
                    Path = filePath,
                    Name = fileInfo.Name,
                    Extension = fileInfo.Extension,
                    Size = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTime,
                    ModifiedAt = fileInfo.LastWriteTime
                };
                _databaseService.UpsertFile(entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding file to index: {ex.Message}");
            }
        }

        private void AddFolderToIndex(string folderPath)
        {
            if (_databaseService == null) return;

            try
            {
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    AddFileToIndex(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding folder to index: {ex.Message}");
            }
        }

        private string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
                return path;

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
            if (!Directory.Exists(path))
                return path;

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

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var targetSubDir = Path.Combine(targetDir, dirName);
                CopyDirectory(dir, targetSubDir);
            }
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            {
                dropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // Dateien AUS der App ziehen - NUR KOPIEREN
        private void DgFiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void DgFiles_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_isDragging) return;
                _isDragging = true;

                var selectedItems = dgFiles.SelectedItems.Cast<FileEntry>()
                    .Where(f => (f.IsFolder && Directory.Exists(f.Path)) || (!f.IsFolder && File.Exists(f.Path)))
                    .Select(f => f.Path)
                    .ToArray();

                if (selectedItems.Length > 0)
                {
                    var data = new DataObject();
                    var fileDropList = new StringCollection();
                    fileDropList.AddRange(selectedItems);
                    data.SetFileDropList(fileDropList);

                    // NUR COPY erlauben wenn aus der App gezogen wird
                    DragDrop.DoDragDrop(dgFiles, data, DragDropEffects.Copy);
                }

                _isDragging = false;
            }
        }

        #endregion

        private async Task OpenFolderAsync(string folderPath)
        {
            _currentRootFolder = folderPath;
            _currentViewFolder = _currentRootFolder;
            txtCurrentFolder.Text = $"📁 {_currentRootFolder}";
            _settingsService.AddRecentFolder(_currentRootFolder);
            
            _backHistory.Clear();
            _forwardHistory.Clear();
            
            btnRefresh.IsEnabled = true;
            btnExport.IsEnabled = true;

            InitializeDatabaseForFolder(_currentRootFolder);

            if (HasExistingIndex(_currentRootFolder) && _databaseService.GetFileCount() > 0)
            {
                var result = MessageBox.Show(
                    $"This folder already has an index with {_databaseService.GetFileCount()} files.\n\n" +
                    "Do you want to refresh the index?\n\n" +
                    "• Yes = Refresh index (update changes)\n" +
                    "• No = Use existing index",
                    "Existing Index Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await StartIndexing();
                }
                else
                {
                    NavigateToFolder(_currentRootFolder, addToHistory: false);
                    txtStatus.Text = "Loaded existing index";
                }
            }
            else
            {
                await StartIndexing();
            }
        }

        private void NavigateToFolder(string folderPath, bool addToHistory = true)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            if (addToHistory && !string.IsNullOrEmpty(_currentViewFolder) && _currentViewFolder != folderPath)
            {
                _backHistory.Push(_currentViewFolder);
                _forwardHistory.Clear();
            }

            _currentViewFolder = folderPath;
            _isSearchMode = false;

            UpdateNavigationButtons();
            UpdateBreadcrumbs();
            RefreshCurrentFolderView();
        }

        private void RefreshCurrentFolderView()
        {
            if (_databaseService == null || string.IsNullOrEmpty(_currentViewFolder))
            {
                dgFiles.ItemsSource = null;
                txtFileCount.Text = "0 items";
                return;
            }

            var items = new List<FileEntry>();

            try
            {
                var directories = Directory.GetDirectories(_currentViewFolder)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .OrderBy(d => Path.GetFileName(d));

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
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }

            var allFiles = _databaseService.GetAllFiles();
            var filesInFolder = allFiles
                .Where(f => Path.GetDirectoryName(f.Path) == _currentViewFolder)
                .OrderBy(f => f.Name);

            items.AddRange(filesInFolder);

            dgFiles.ItemsSource = items;
            
            var folderCount = items.Count(i => i.IsFolder);
            var fileCount = items.Count(i => !i.IsFolder);
            txtFileCount.Text = $"{folderCount} folders, {fileCount} files";
        }

        private void UpdateBreadcrumbs()
        {
            pnlBreadcrumbs.Children.Clear();

            if (string.IsNullOrEmpty(_currentViewFolder) || string.IsNullOrEmpty(_currentRootFolder))
                return;

            var relativePath = _currentViewFolder;
            if (_currentViewFolder.StartsWith(_currentRootFolder))
            {
                relativePath = _currentViewFolder.Substring(_currentRootFolder.Length).TrimStart('\\');
            }

            var rootButton = new Button
            {
                Content = Path.GetFileName(_currentRootFolder),
                Style = (Style)FindResource("BreadcrumbButtonStyle"),
                Tag = _currentRootFolder
            };
            rootButton.Click += BreadcrumbButton_Click;
            pnlBreadcrumbs.Children.Add(rootButton);

            if (!string.IsNullOrEmpty(relativePath))
            {
                var parts = relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var currentPath = _currentRootFolder;

                foreach (var part in parts)
                {
                    pnlBreadcrumbs.Children.Add(new TextBlock
                    {
                        Text = " › ",
                        Foreground = (Brush)FindResource("ForegroundBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0.5
                    });

                    currentPath = Path.Combine(currentPath, part);
                    var button = new Button
                    {
                        Content = part,
                        Style = (Style)FindResource("BreadcrumbButtonStyle"),
                        Tag = currentPath
                    };
                    button.Click += BreadcrumbButton_Click;
                    pnlBreadcrumbs.Children.Add(button);
                }
            }
        }

        private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                NavigateToFolder(path);
            }
        }

        private void UpdateNavigationButtons()
        {
            btnBack.IsEnabled = _backHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
            btnUp.IsEnabled = !string.IsNullOrEmpty(_currentViewFolder) && 
                              _currentViewFolder != _currentRootFolder &&
                              Directory.GetParent(_currentViewFolder)?.FullName != null;
        }

        private void RefreshFileList()
        {
            if (_isSearchMode)
                return;
            RefreshCurrentFolderView();
        }

        private async void UpdateLicenseStatus()
        {
            var storedKey = _licenseService.GetStoredLicenseKey();
            if (!string.IsNullOrEmpty(storedKey))
            {
                var license = await _licenseService.CheckLicenseAsync(storedKey);
                UpdateLicenseUI(license);
            }
            else
            {
                licenseIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff9800"));
                txtLicenseStatus.Text = "No License";
            }
        }

        private void UpdateLicenseUI(LicenseInfo license)
        {
            if (license.Valid)
            {
                licenseIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4caf50"));
                txtLicenseStatus.Text = license.ExpiresAt.HasValue 
                    ? $"Licensed until {license.ExpiresAt.Value:yyyy-MM-dd}" 
                    : "Licensed";
            }
            else
            {
                licenseIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f44336"));
                txtLicenseStatus.Text = string.IsNullOrEmpty(license.ErrorMessage) 
                    ? "Invalid License" 
                    : license.ErrorMessage;
            }
        }

        #region Navigation Event Handlers

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(_currentViewFolder);
                var previousFolder = _backHistory.Pop();
                NavigateToFolder(previousFolder, addToHistory: false);
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(_currentViewFolder);
                var nextFolder = _forwardHistory.Pop();
                NavigateToFolder(nextFolder, addToHistory: false);
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentViewFolder))
            {
                var parent = Directory.GetParent(_currentViewFolder)?.FullName;
                if (!string.IsNullOrEmpty(parent) && parent.StartsWith(_currentRootFolder))
                {
                    NavigateToFolder(parent);
                }
                else if (_currentViewFolder != _currentRootFolder)
                {
                    NavigateToFolder(_currentRootFolder);
                }
            }
        }

        private void DgFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedItem = dgFiles.SelectedItem as FileEntry;
            if (selectedItem == null) return;

            if (selectedItem.IsFolder)
            {
                NavigateToFolder(selectedItem.Path);
            }
            else
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = selectedItem.Path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Event Handlers

        private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select folder to index";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    await OpenFolderAsync(dialog.SelectedPath);
                }
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentRootFolder))
            {
                await StartIndexing();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_exportService == null)
            {
                MessageBox.Show("Please select a folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|Compressed JSON (*.json.gz)|*.json.gz",
                DefaultExt = ".json",
                FileName = "index.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".gz"))
                    {
                        _exportService.ExportToGzip(dialog.FileName, _currentRootFolder);
                    }
                    else
                    {
                        _exportService.ExportToJson(dialog.FileName, _currentRootFolder);
                    }

                    txtStatus.Text = $"Index exported to {dialog.FileName}";
                    MessageBox.Show($"Index successfully exported to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            var licenseWindow = new LicenseWindow(_licenseService, _exportService, _currentRootFolder);
            licenseWindow.Owner = this;
            licenseWindow.ShowDialog();
            UpdateLicenseStatus();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            PerformSearch();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Text = "";
            _isSearchMode = false;
            NavigateToFolder(_currentViewFolder, addToHistory: false);
            txtStatus.Text = "Search cleared";
        }

        private void TxtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformSearch();
            }
        }

        private void PerformSearch()
        {
            if (_databaseService == null)
            {
                MessageBox.Show("Please select a folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                _isSearchMode = false;
                RefreshCurrentFolderView();
                return;
            }

            _isSearchMode = true;
            List<FileEntry> results;
            
            if (query.StartsWith("#"))
            {
                var tagName = query.Substring(1);
                results = _databaseService.SearchByTag(tagName);
            }
            else
            {
                results = _databaseService.SearchFiles(query);
            }

            dgFiles.ItemsSource = results;
            txtFileCount.Text = $"{results.Count} files found";
            txtStatus.Text = $"Search results for: {query}";
        }

        private void DgFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedFile = dgFiles.SelectedItem as FileEntry;
            UpdateDetailsPanel();
        }

        private void UpdateDetailsPanel()
        {
            if (_selectedFile == null || _selectedFile.IsFolder)
            {
                txtNoSelection.Visibility = Visibility.Visible;
                pnlFileDetails.Visibility = Visibility.Collapsed;
                txtNoSelection.Text = _selectedFile?.IsFolder == true ? "Folder selected - double-click to open" : "No file selected";
                return;
            }

            txtNoSelection.Visibility = Visibility.Collapsed;
            pnlFileDetails.Visibility = Visibility.Visible;

            txtFileName.Text = _selectedFile.Name;
            txtFilePath.Text = _selectedFile.Path;
            txtFileSize.Text = _selectedFile.SizeDisplay;
            txtFileCreated.Text = _selectedFile.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            txtFileModified.Text = _selectedFile.ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");
            txtTags.Text = string.Join(", ", _selectedFile.Tags);
            txtNotes.Text = _selectedFile.Note ?? "";
        }

        private void BtnSaveTags_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFile == null || _selectedFile.IsFolder || _databaseService == null) return;

            try
            {
                var tags = txtTags.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                _databaseService.SetTagsForFile(_selectedFile.Id, tags);
                _databaseService.SetNoteForFile(_selectedFile.Id, txtNotes.Text);

                _selectedFile.Tags = tags;
                _selectedFile.TagsText = string.Join(", ", tags);
                _selectedFile.Note = txtNotes.Text;

                dgFiles.Items.Refresh();
                txtStatus.Text = "Tags and notes saved successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFile == null) return;

            try
            {
                var directory = Path.GetDirectoryName(_selectedFile.Path);
                if (Directory.Exists(directory))
                {
                    Process.Start("explorer.exe", $"/select,\"{_selectedFile.Path}\"");
                }
                else
                {
                    MessageBox.Show("File location not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelIndex_Click(object sender, RoutedEventArgs e)
        {
            _indexCancellation?.Cancel();
        }

        #endregion

        #region Indexing

        private async Task StartIndexing()
        {
            if (_isIndexing || _indexerService == null) return;

            _isIndexing = true;
            _indexCancellation = new CancellationTokenSource();

            btnSelectFolder.IsEnabled = false;
            btnRefresh.IsEnabled = false;
            pnlProgress.Visibility = Visibility.Visible;
            progressBar.Value = 0;

            try
            {
                var result = await _indexerService.ScanFolderAsync(_currentRootFolder, _indexCancellation.Token);

                if (result.Success)
                {
                    txtStatus.Text = $"Indexed {result.FilesIndexed} files in {result.Duration.TotalSeconds:F1}s";
                }
                else if (result.Cancelled)
                {
                    txtStatus.Text = "Indexing cancelled";
                }
                else
                {
                    txtStatus.Text = $"Indexing failed: {result.ErrorMessage}";
                }

                if (result.Errors.Count > 0)
                {
                    Debug.WriteLine($"Indexing errors: {string.Join(", ", result.Errors.Take(10))}");
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isIndexing = false;
                btnSelectFolder.IsEnabled = true;
                btnRefresh.IsEnabled = true;
                pnlProgress.Visibility = Visibility.Collapsed;
                _indexCancellation?.Dispose();
                _indexCancellation = null;

                NavigateToFolder(_currentViewFolder, addToHistory: false);
            }
        }

        private void IndexerService_ProgressChanged(object sender, IndexProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = e.Percentage;
                txtProgressStatus.Text = $"Indexing: {e.Current}/{e.Total} files ({e.Percentage:F0}%)";
            });
        }

        private void IndexerService_StatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                txtProgressStatus.Text = status;
            });
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _indexCancellation?.Cancel();
            _databaseService?.Dispose();
            base.OnClosed(e);
        }
    }
}
