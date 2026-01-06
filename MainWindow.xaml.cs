using System;
using System.Collections.Generic;
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
        private string _currentViewFolder;  // Aktuell angezeigter Ordner
        private bool _isIndexing;
        private bool _isSearchMode;

        // Navigation History
        private Stack<string> _backHistory = new Stack<string>();
        private Stack<string> _forwardHistory = new Stack<string>();

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
                txtStatus.Text = "Ready - Select a folder to start indexing";
            }

            UpdateLicenseStatus();
        }

        /// <summary>
        /// Navigiert zu einem Ordner und zeigt dessen Inhalt an
        /// </summary>
        private void NavigateToFolder(string folderPath, bool addToHistory = true)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            // History aktualisieren
            if (addToHistory && !string.IsNullOrEmpty(_currentViewFolder) && _currentViewFolder != folderPath)
            {
                _backHistory.Push(_currentViewFolder);
                _forwardHistory.Clear();
            }

            _currentViewFolder = folderPath;
            _isSearchMode = false;

            // Navigation Buttons aktualisieren
            UpdateNavigationButtons();
            UpdateBreadcrumbs();
            
            // Ordnerinhalt laden
            RefreshCurrentFolderView();
        }

        /// <summary>
        /// Aktualisiert die Anzeige des aktuellen Ordners
        /// </summary>
        private void RefreshCurrentFolderView()
        {
            if (_databaseService == null || string.IsNullOrEmpty(_currentViewFolder))
            {
                dgFiles.ItemsSource = null;
                txtFileCount.Text = "0 items";
                return;
            }

            var items = new List<FileEntry>();

            // Unterordner hinzufügen (aus Dateisystem)
            try
            {
                var directories = Directory.GetDirectories(_currentViewFolder)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))  // Versteckte Ordner ausblenden
                    .OrderBy(d => Path.GetFileName(d));

                foreach (var dir in directories)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    items.Add(new FileEntry
                    {
                        Id = -1,  // Ordner haben keine DB-ID
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

            // Dateien aus der Datenbank laden (nur für aktuellen Ordner)
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

        /// <summary>
        /// Aktualisiert die Breadcrumb-Navigation
        /// </summary>
        private void UpdateBreadcrumbs()
        {
            pnlBreadcrumbs.Children.Clear();

            if (string.IsNullOrEmpty(_currentViewFolder) || string.IsNullOrEmpty(_currentRootFolder))
                return;

            // Relativen Pfad berechnen
            var relativePath = _currentViewFolder;
            if (_currentViewFolder.StartsWith(_currentRootFolder))
            {
                relativePath = _currentViewFolder.Substring(_currentRootFolder.Length).TrimStart('\\');
            }

            // Root-Button
            var rootButton = new Button
            {
                Content = Path.GetFileName(_currentRootFolder),
                Style = (Style)FindResource("BreadcrumbButtonStyle"),
                Tag = _currentRootFolder
            };
            rootButton.Click += BreadcrumbButton_Click;
            pnlBreadcrumbs.Children.Add(rootButton);

            // Pfadteile
            if (!string.IsNullOrEmpty(relativePath))
            {
                var parts = relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var currentPath = _currentRootFolder;

                foreach (var part in parts)
                {
                    // Separator
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
                // In Ordner navigieren
                NavigateToFolder(selectedItem.Path);
            }
            else
            {
                // Datei öffnen
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
                    _currentRootFolder = dialog.SelectedPath;
                    _currentViewFolder = _currentRootFolder;
                    txtCurrentFolder.Text = $"📁 {_currentRootFolder}";
                    _settingsService.AddRecentFolder(_currentRootFolder);
                    
                    // History zurücksetzen
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
                    txtStatus.Text = $"Indexed {result.FilesIndexed} files in {result.Duration.TotalSeconds:F1}s - Index saved to folder";
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
