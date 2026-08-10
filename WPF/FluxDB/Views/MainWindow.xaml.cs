using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluxDB.Models;
using FluxDB.Services;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;

namespace FluxDB.Views
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private SettingsService _settingsService;
        private DatabaseService _databaseService;
        private IndexerService _indexerService;
        private ExportService _exportService;

        private CancellationTokenSource _indexCancellation;
        private CancellationTokenSource _previewCts;
        private int _previewVersion;
        private FileEntry _selectedFile;
        private string _currentRootFolder;
        private string _currentViewFolder;
        private bool _isIndexing;

        // Navigation History
        private Stack<string> _backHistory = new Stack<string>();
        private Stack<string> _forwardHistory = new Stack<string>();
        private const int MaxHistorySize = 50;

        // Drag & Drop
        private Point _dragStartPoint;
        private bool _isDragging;

        // Clipboard
        private List<string> _clipboardFiles = new List<string>();
        private bool _clipboardIsCut = false;

        // Filter
        private string _currentFilter = "All Files";

        private const string DatabaseFileName = ".fluxdb";

        private static readonly HashSet<string> ImageExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".svg" };
        private static readonly HashSet<string> AudioExtensions = new HashSet<string> { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };
        private static readonly HashSet<string> VideoExtensions = new HashSet<string> { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };
        private static readonly HashSet<string> DocumentExtensions = new HashSet<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt" };
        private static readonly HashSet<string> ArchiveExtensions = new HashSet<string> { ".zip", ".rar", ".7z", ".tar", ".gz" };
        private static readonly HashSet<string> CodeExtensions = new HashSet<string> { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xaml", ".xml", ".json", ".sql" };
        private static readonly HashSet<string> PreviewTextExtensions = new HashSet<string> { ".txt", ".md", ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".xaml", ".xml", ".json", ".sql", ".log", ".ini", ".cfg", ".bat", ".ps1", ".sh", ".yml", ".yaml", ".toml", ".config" };

        private static readonly Guid ShellItemIID = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        public MainWindow()
        {
            InitializeComponent();

            btnBack.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowLeft24);
            btnForward.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowRight24);
            btnUp.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowUp24);

            // Load application icon from executable folder if available
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var icoPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = BitmapFrame.Create(new Uri(icoPath));
                }
            }
            catch (Exception ex) { LoggingService.Log($"Failed to load window icon: {ex.Message}"); }

            InitializeServices();
            LoadInitialData();
        }

        private void InitializeServices()
        {
            _settingsService = new SettingsService();
            _databaseService = null;
            _indexerService = null;
            _exportService = null;
        }

        private void InitializeDatabaseForFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path must not be empty.", nameof(folderPath));

            _databaseService?.Dispose();
            if (_indexerService != null)
            {
                _indexerService.ProgressChanged -= IndexerService_ProgressChanged;
                _indexerService.StatusChanged -= IndexerService_StatusChanged;
            }
            var dbPath = Path.Combine(folderPath, DatabaseFileName);
            _databaseService = new DatabaseService(dbPath);
            _indexerService = new IndexerService(_databaseService);
            _exportService = new ExportService(_databaseService, _settingsService);
            _indexerService.ProgressChanged += IndexerService_ProgressChanged;
            _indexerService.StatusChanged += IndexerService_StatusChanged;

            try
            {
                if (File.Exists(dbPath))
                {
                    var attr = File.GetAttributes(dbPath);
                    if ((attr & FileAttributes.Hidden) == 0)
                        File.SetAttributes(dbPath, attr | FileAttributes.Hidden);
                }
            }
            catch (Exception ex) { LoggingService.Log($"Failed to hide database file: {ex.Message}"); }

            try
            {
                _settingsService.AddRecentFolder(folderPath);
            }
            catch (Exception ex) { LoggingService.Log($"Failed to add recent folder: {ex.Message}"); }
        }

        private bool HasExistingIndex(string folderPath)
        {
            var dbPath = Path.Combine(folderPath, DatabaseFileName);
            return File.Exists(dbPath);
        }

        private void LoadInitialData()
        {
            try
            {
                var settings = _settingsService.Load();
                var persistence = settings.Persistence ?? new PersistenceOptions();

                if (persistence.LastRootFolder && !string.IsNullOrEmpty(settings.LastRootFolder) && Directory.Exists(settings.LastRootFolder))
                {
                    _currentRootFolder = settings.LastRootFolder;

                    string initialView = _currentRootFolder;
                    if (persistence.LastViewFolder &&
                        settings.FolderLastView != null &&
                        settings.FolderLastView.TryGetValue(_currentRootFolder, out var lastView) &&
                        !string.IsNullOrEmpty(lastView) &&
                        Directory.Exists(lastView))
                    {
                        var rootFull = Path.GetFullPath(_currentRootFolder)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var lastFull = Path.GetFullPath(lastView)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        var rootPrefix = rootFull + Path.DirectorySeparatorChar;
                        if (string.Equals(lastFull, rootFull, StringComparison.OrdinalIgnoreCase) ||
                            lastFull.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            initialView = lastFull;
                        }
                    }
                    _currentViewFolder = initialView;
                    txtCurrentFolder.Text = initialView;
                    btnRefresh.IsEnabled = true;

                    InitializeDatabaseForFolder(_currentRootFolder);

                    if (persistence.Filter)
                        LoadFilterForFolder(_currentRootFolder);
                    else
                    {
                        _currentFilter = "All Files";
                        cmbFilter.SelectedIndex = 0;
                    }

                    NavigateToFolder(initialView, addToHistory: false);

                    if (persistence.Sort)
                        ApplySavedSort(_currentRootFolder);

                    if (persistence.ColumnVisibility)
                        ApplySavedColumnVisibility(settings);

                    if (HasExistingIndex(_currentRootFolder))
                    {
                        txtStatus.Text = "Index loaded from folder";
                    }
                }
                else
                {
                    txtStatus.Text = "Ready - Select a folder or drag & drop to start";
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"LoadInitialData failed: {ex}");
                txtStatus.Text = "Failed to load initial data - select a folder to continue";
            }
        }

        #region Keyboard Shortcuts

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                if (e.Key == Key.Escape)
                {
                    dgFiles.Focus();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedFiles(false);
                e.Handled = true;
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedFiles(true);
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PasteFiles();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedFiles();
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                RenameSelectedFile();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                RefreshCurrentFolderView();
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                txtSearch.Focus();
                txtSearch.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                BtnBack_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                BtnForward_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                BtnUp_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OpenSelectedItem();
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                BtnUp_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.F8)
            {
                ShowLogViewer();
                e.Handled = true;
            }
            else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleCheatSheet();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (cheatSheetOverlay.Visibility == Visibility.Visible)
                {
                    cheatSheetOverlay.Visibility = Visibility.Collapsed;
                    dgFiles.Focus();
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Clipboard Operations

        private void CopySelectedFiles(bool cut)
        {
            var selected = dgFiles.SelectedItems.Cast<FileEntry>().ToList();
            if (selected.Count == 0) return;

            _clipboardFiles = selected.Select(f => f.Path).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            _clipboardIsCut = cut;

            var fileCollection = new StringCollection();
            fileCollection.AddRange(_clipboardFiles.ToArray());
            Clipboard.SetFileDropList(fileCollection);

            txtStatus.Text = cut ? $"Cut {_clipboardFiles.Count} item(s)" : $"Copied {_clipboardFiles.Count} item(s)";
            ShowToast(cut ? $"Cut {_clipboardFiles.Count} item(s)" : $"Copied {_clipboardFiles.Count} item(s)");
        }

        private async void PasteFiles()
        {
            if (string.IsNullOrEmpty(_currentViewFolder)) return;

            List<string> filesToPaste = new List<string>();

            if (Clipboard.ContainsFileDropList())
            {
                filesToPaste = Clipboard.GetFileDropList().Cast<string>().ToList();
            }
            else if (_clipboardFiles.Count > 0)
            {
                filesToPaste = _clipboardFiles;
            }

            if (filesToPaste.Count == 0)
            {
                txtStatus.Text = "Nothing to paste";
                return;
            }

            await CopyOrMoveFilesAsync(filesToPaste.ToArray(), _currentViewFolder, _clipboardIsCut);

            if (_clipboardIsCut)
            {
                _clipboardFiles.Clear();
                _clipboardIsCut = false;
            }
        }

        private async void DeleteSelectedFiles()
        {
            var selected = dgFiles.SelectedItems.Cast<FileEntry>().ToList();
            if (selected.Count == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selected.Count} item(s)?\n\nThis action cannot be undone!",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var deletedCount = 0;
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    try
                    {
                        if (item.IsFolder && Directory.Exists(item.Path))
                        {
                            if (_databaseService != null && item.Id > 0)
                            {
                                _databaseService.MarkPathAsDeleted(item.Path);
                            }
                            Directory.Delete(item.Path, true);
                            Interlocked.Increment(ref deletedCount);
                        }
                        else if (File.Exists(item.Path))
                        {
                            File.Delete(item.Path);

                            if (_databaseService != null && item.Id > 0)
                            {
                                _databaseService.MarkFileAsDeleted(item.Id);
                            }
                            Interlocked.Increment(ref deletedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting {item.Path}: {ex.Message}");
                        Dispatcher.BeginInvoke(new Action(() =>
                            MessageBox.Show($"Could not delete {item.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error)));
                    }
                }
            });

            await RefreshCurrentFolderViewAsync();
            txtStatus.Text = $"Deleted {deletedCount} item(s)";
            ShowToast($"Deleted {deletedCount} item(s)");
        }

        private void RenameSelectedFile()
        {
            var selected = dgFiles.SelectedItem as FileEntry;
            if (selected == null) return;

            var currentName = selected.IsFolder ? selected.Name : Path.GetFileNameWithoutExtension(selected.Name);
            var extension = selected.IsFolder ? "" : Path.GetExtension(selected.Name);

            var dialog = new RenameDialog(currentName);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var newName = dialog.NewName + extension;
                var newPath = Path.Combine(Path.GetDirectoryName(selected.Path), newName);

                try
                {
                    if (selected.IsFolder)
                    {
                        Directory.Move(selected.Path, newPath);
                        if (_databaseService != null)
                        {
                            _databaseService.UpdateFolderPath(selected.Path, newPath);
                        }
                    }
                    else
                    {
                        File.Move(selected.Path, newPath);

                        if (_databaseService != null && selected.Id > 0)
                        {
                            _databaseService.UpdateFilePathAndName(selected.Id, newPath, newName);
                        }
                    }

                    RefreshCurrentFolderView();
                    txtStatus.Text = $"Renamed to {newName}";
                    ShowToast($"Renamed to {newName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not rename: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CreateNewFolder()
        {
            if (string.IsNullOrEmpty(_currentViewFolder)) return;

            var dialog = new RenameDialog("New Folder");
            dialog.Owner = this;
            dialog.Title = "New Folder";

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var newPath = Path.Combine(_currentViewFolder, dialog.NewName);
                newPath = GetUniqueFolderPath(newPath);

                try
                {
                    Directory.CreateDirectory(newPath);
                    RefreshCurrentFolderView();
                    txtStatus.Text = $"Created folder: {Path.GetFileName(newPath)}";
                    ShowToast($"Created folder: {Path.GetFileName(newPath)}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not create folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Context Menu

        private void ContextMenu_Open(object sender, RoutedEventArgs e)
        {
            OpenSelectedItem();
        }

        private void ContextMenu_OpenLocation(object sender, RoutedEventArgs e)
        {
            BtnOpenFile_Click(null, null);
        }

        private void ContextMenu_Copy(object sender, RoutedEventArgs e)
        {
            CopySelectedFiles(false);
        }

        private void ContextMenu_Cut(object sender, RoutedEventArgs e)
        {
            CopySelectedFiles(true);
        }

        private void ContextMenu_Paste(object sender, RoutedEventArgs e)
        {
            PasteFiles();
        }

        private void ContextMenu_Rename(object sender, RoutedEventArgs e)
        {
            RenameSelectedFile();
        }

        private void ContextMenu_Delete(object sender, RoutedEventArgs e)
        {
            DeleteSelectedFiles();
        }

        private void ContextMenu_NewFolder(object sender, RoutedEventArgs e)
        {
            CreateNewFolder();
        }

        private void ContextMenu_Properties(object sender, RoutedEventArgs e)
        {
            var selected = dgFiles.SelectedItem as FileEntry;
            if (selected == null) return;

            var info = new ProcessStartInfo("explorer.exe", $"/select,\"{selected.Path}\"");
            using (var proc = Process.Start(info)) { }
        }

        #endregion

        #region Filter

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgFiles == null || cmbFilter == null) return;

            if (cmbFilter.SelectedItem is ComboBoxItem item)
            {
                _currentFilter = item.Content.ToString();

                if (!string.IsNullOrEmpty(_currentRootFolder))
                {
                    SaveFilterForFolder(_currentRootFolder, _currentFilter);
                }

                if (_databaseService != null)
                {
                    RefreshCurrentFolderView();
                }
            }
        }

        private void SaveFilterForFolder(string folderPath, string filter)
        {
            var settings = _settingsService.Load();
            if (settings.Persistence != null && !settings.Persistence.Filter) return;
            if (settings.FolderFilters == null)
            {
                settings.FolderFilters = new Dictionary<string, string>();
            }
            settings.FolderFilters[folderPath] = filter;
            _settingsService.Save(settings);
        }

        private void LoadFilterForFolder(string folderPath)
        {
            try
            {
                var settings = _settingsService.Load();
                if (settings.FolderFilters != null && settings.FolderFilters.TryGetValue(folderPath, out var filter))
                {
                    _currentFilter = filter;

                    foreach (ComboBoxItem item in cmbFilter.Items)
                    {
                        if (item.Content.ToString() == filter)
                        {
                            cmbFilter.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    _currentFilter = "All Files";
                    cmbFilter.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"LoadFilterForFolder failed: {ex}");
                _currentFilter = "All Files";
                if (cmbFilter != null) cmbFilter.SelectedIndex = 0;
            }
        }

        private bool MatchesFilter(FileEntry entry)
        {
            if (entry.IsFolder) return true;
            if (_currentFilter == "All Files") return true;

            var ext = (entry.Extension ?? "").ToLower();

            switch (_currentFilter)
            {
                case "Images": return ImageExtensions.Contains(ext);
                case "Audio": return AudioExtensions.Contains(ext);
                case "Video": return VideoExtensions.Contains(ext);
                case "Documents": return DocumentExtensions.Contains(ext);
                case "Archives": return ArchiveExtensions.Contains(ext);
                case "Code": return CodeExtensions.Contains(ext);
                case "Tags": return entry.Tags.Count > 0;
                default: return true;
            }
        }

        #endregion

        #region Preview

        private async void UpdatePreview(FileEntry file)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = new CancellationTokenSource();
            var ct = _previewCts.Token;
            var version = Interlocked.Increment(ref _previewVersion);

            imgPreview.Source = null;
            imgPreview.Visibility = Visibility.Collapsed;
            txtPreviewScroll.Visibility = Visibility.Collapsed;
            txtNoPreview.Visibility = Visibility.Collapsed;
            pnlPreview.Visibility = Visibility.Collapsed;
            webPdfPreview.Visibility = Visibility.Collapsed;
            if (webPdfPreview.CoreWebView2 != null)
                webPdfPreview.CoreWebView2.Navigate("about:blank");

            if (file == null || file.IsFolder || !File.Exists(file.Path))
            {
                pnlPreview.Visibility = Visibility.Collapsed;
                return;
            }

            pnlPreview.Visibility = Visibility.Visible;
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

                    if (_previewVersion != version) return;

                    imgPreview.Source = bitmap;
                    imgPreview.Visibility = Visibility.Visible;
                    var scale2 = imgPreview.LayoutTransform as ScaleTransform;
                    if (scale2 != null) { scale2.ScaleX = 1; scale2.ScaleY = 1; }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    LoggingService.Log($"Cannot load image preview: {ex.Message}");
                    txtNoPreview.Text = "Cannot load image";
                    txtNoPreview.Visibility = Visibility.Visible;
                }
            }
            else if (ext == ".pdf")
            {
                try
                {
                    await webPdfPreview.EnsureCoreWebView2Async();
                    webPdfPreview.CoreWebView2.Navigate(file.Path);
                    webPdfPreview.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    LoggingService.Log($"Cannot preview PDF: {ex.Message}");
                    txtNoPreview.Text = "Cannot preview PDF";
                    txtNoPreview.Visibility = Visibility.Visible;
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
                            using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                            {
                                var text = reader.ReadToEnd();

                                if (reader.CurrentEncoding == Encoding.UTF8 && text.Contains("\ufffd"))
                                {
                                    fs.Position = 0;
                                    using (var readerAnsi = new StreamReader(fs, Encoding.Default))
                                    {
                                        text = readerAnsi.ReadToEnd();
                                    }
                                }

                                if (text.Length > 5000)
                                    text = text.Substring(0, 5000) + "\n\n... (truncated)";

                                return text;
                            }
                        }
                        catch
                        {
                            return null;
                        }
                    }, ct);
                }
                catch (OperationCanceledException) { return; }

                if (_previewVersion != version) return;

                if (content != null)
                {
                    txtPreview.Text = content;
                    txtPreviewScroll.Visibility = Visibility.Visible;
                }
                else
                {
                    txtNoPreview.Text = "Cannot read file";
                    txtNoPreview.Visibility = Visibility.Visible;
                }
            }
            else
            {
                txtNoPreview.Text = "No preview available";
                txtNoPreview.Visibility = Visibility.Visible;
            }
        }

        private void SetImageSource(BitmapSource bitmap)
        {
            imgPreview.Source = bitmap;
            imgPreview.Visibility = Visibility.Visible;
            var scale = imgPreview.LayoutTransform as ScaleTransform;
            if (scale != null) { scale.ScaleX = 1; scale.ScaleY = 1; }
        }

        private void ImgPreviewContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (imgPreview.Visibility != Visibility.Visible || imgPreview.Source == null) return;

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                var scale = imgPreview.LayoutTransform as ScaleTransform;
                if (scale == null) return;

                var zoomFactor = e.Delta > 0 ? 1.2 : 0.8;
                scale.ScaleX *= zoomFactor;
                scale.ScaleY *= zoomFactor;

                if (scale.ScaleX < 0.1) { scale.ScaleX = 0.1; scale.ScaleY = 0.1; }
                if (scale.ScaleX > 10) { scale.ScaleX = 10; scale.ScaleY = 10; }

                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                imgPreviewContainer.ScrollToHorizontalOffset(
                    imgPreviewContainer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        #endregion

        #region Column Configuration

        private void ColumnHeader_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var header = sender as DataGridColumnHeader;
            if (header?.Column == null) return;

            var menu = new System.Windows.Controls.ContextMenu();
            foreach (var col in dgFiles.Columns)
            {
                var headerText = col.Header?.ToString() ?? "";
                if (string.IsNullOrEmpty(headerText)) continue;

                var menuItem = new System.Windows.Controls.MenuItem
                {
                    Header = headerText,
                    IsCheckable = true,
                    IsChecked = col.Visibility == Visibility.Visible,
                    Tag = col
                };
                menuItem.Click += (s2, e2) =>
                {
                    if (s2 is System.Windows.Controls.MenuItem mi && mi.Tag is DataGridColumn dgc)
                    {
                        dgc.Visibility = mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                        SaveColumnVisibility();
                    }
                };
                menu.Items.Add(menuItem);
            }

            menu.Items.Add(new System.Windows.Controls.Separator());
            var resetItem = new System.Windows.Controls.MenuItem { Header = "Reset All" };
            resetItem.Click += (s2, e2) =>
            {
                foreach (var col in dgFiles.Columns)
                    col.Visibility = Visibility.Visible;
                SaveColumnVisibility();
            };
            menu.Items.Add(resetItem);

            menu.IsOpen = true;
        }

        private void SaveColumnVisibility()
        {
            if (_settingsService == null) return;
            var settings = _settingsService.Load();
            if (settings.Persistence != null && !settings.Persistence.ColumnVisibility) return;
            if (settings.ColumnVisibility == null)
                settings.ColumnVisibility = new Dictionary<string, bool>();
            foreach (var col in dgFiles.Columns)
            {
                var header = col.Header?.ToString() ?? "";
                if (!string.IsNullOrEmpty(header))
                    settings.ColumnVisibility[header] = col.Visibility == Visibility.Visible;
            }
            _settingsService.Save(settings);
        }

        private void ApplySavedColumnVisibility(AppSettings settings)
        {
            try
            {
                if (settings?.ColumnVisibility == null || settings.ColumnVisibility.Count == 0) return;
                foreach (var col in dgFiles.Columns)
                {
                    var header = col.Header?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(header) && settings.ColumnVisibility.TryGetValue(header, out var visible))
                    {
                        col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex) { LoggingService.Log($"ApplySavedColumnVisibility failed: {ex.Message}"); }
        }

        private void ApplySavedSort(string rootFolder)
        {
            try
            {
                var settings = _settingsService.Load();
                if (settings?.FolderSortColumn == null || settings.FolderSortDirection == null) return;
                if (!settings.FolderSortColumn.TryGetValue(rootFolder, out var sortPath) || string.IsNullOrEmpty(sortPath)) return;
                if (!settings.FolderSortDirection.TryGetValue(rootFolder, out var dirStr)) return;

                var direction = string.Equals(dirStr, "Descending", StringComparison.OrdinalIgnoreCase)
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                var view = CollectionViewSource.GetDefaultView(dgFiles.ItemsSource);
                if (view != null)
                {
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription(sortPath, direction));
                }

                foreach (var col in dgFiles.Columns)
                {
                    col.SortDirection = string.Equals(col.SortMemberPath, sortPath, StringComparison.OrdinalIgnoreCase)
                        ? direction
                        : (ListSortDirection?)null;
                }
            }
            catch (Exception ex) { LoggingService.Log($"ApplySavedSort failed: {ex.Message}"); }
        }

        private void SaveSortForFolder(string sortPath, ListSortDirection direction)
        {
            if (string.IsNullOrEmpty(_currentRootFolder) || string.IsNullOrEmpty(sortPath)) return;
            try
            {
                var settings = _settingsService.Load();
                if (settings.Persistence != null && !settings.Persistence.Sort) return;
                if (settings.FolderSortColumn == null)
                    settings.FolderSortColumn = new Dictionary<string, string>();
                if (settings.FolderSortDirection == null)
                    settings.FolderSortDirection = new Dictionary<string, string>();
                settings.FolderSortColumn[_currentRootFolder] = sortPath;
                settings.FolderSortDirection[_currentRootFolder] = direction == ListSortDirection.Descending ? "Descending" : "Ascending";
                _settingsService.Save(settings);
            }
            catch (Exception ex) { LoggingService.Log($"SaveSortForFolder failed: {ex.Message}"); }
        }

        #endregion

        #region Navigation

        private void NavigateToFolder(string folderPath, bool addToHistory = true)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            if (addToHistory && !string.IsNullOrEmpty(_currentViewFolder) && _currentViewFolder != folderPath)
            {
                _backHistory.Push(_currentViewFolder);
                if (_backHistory.Count > MaxHistorySize)
                {
                    var trimmed = _backHistory.ToArray();
                    _backHistory = new Stack<string>(trimmed.Take(MaxHistorySize));
                }
                _forwardHistory.Clear();
            }

            _currentViewFolder = folderPath;
            txtCurrentFolder.Text = folderPath;
            SaveLastViewFolder();
            UpdateNavigationButtons();
            UpdateBreadcrumbs();
            RefreshCurrentFolderView();
        }

        private void SaveLastViewFolder()
        {
            if (string.IsNullOrEmpty(_currentRootFolder) || string.IsNullOrEmpty(_currentViewFolder)) return;
            try
            {
                var settings = _settingsService.Load();
                if (settings.Persistence != null && !settings.Persistence.LastViewFolder) return;
                if (settings.FolderLastView == null)
                    settings.FolderLastView = new Dictionary<string, string>();
                settings.FolderLastView[_currentRootFolder] = _currentViewFolder;
                _settingsService.Save(settings);
            }
            catch (Exception ex) { LoggingService.Log($"SaveLastViewFolder failed: {ex.Message}"); }
        }

        private void UpdateNavigationButtons()
        {
            btnBack.IsEnabled = _backHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
            btnUp.IsEnabled = !string.IsNullOrEmpty(_currentViewFolder) &&
                              _currentViewFolder != _currentRootFolder &&
                              Directory.GetParent(_currentViewFolder)?.FullName != null;
        }

        private void UpdateBreadcrumbs()
        {
            pnlBreadcrumbs.Children.Clear();
            if (string.IsNullOrEmpty(_currentViewFolder) || string.IsNullOrEmpty(_currentRootFolder)) return;

            var relativePath = _currentViewFolder;
            if (_currentViewFolder.StartsWith(_currentRootFolder))
            {
                relativePath = _currentViewFolder.Substring(_currentRootFolder.Length).TrimStart('\\');
            }

            var rootButton = new Button
            {
                Content = Path.GetFileName(_currentRootFolder),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                BorderThickness = new System.Windows.Thickness(0),
                Padding = new System.Windows.Thickness(4, 2, 4, 2),
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = _currentRootFolder
            };
            rootButton.Click += Breadcrumb_Click;
            pnlBreadcrumbs.Children.Add(rootButton);

            if (!string.IsNullOrEmpty(relativePath))
            {
                var parts = relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var currentPath = _currentRootFolder;
                foreach (var part in parts)
                {
                    var sep = new TextBlock
                    {
                        Text = " > ",
                        Foreground = System.Windows.Media.Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 13
                    };
                    pnlBreadcrumbs.Children.Add(sep);

                    currentPath = Path.Combine(currentPath, part);
                    var button = new Button
                    {
                        Content = part,
                        Background = System.Windows.Media.Brushes.Transparent,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                        BorderThickness = new System.Windows.Thickness(0),
                        Padding = new System.Windows.Thickness(4, 2, 4, 2),
                        FontSize = 13,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = currentPath
                    };
                    button.Click += Breadcrumb_Click;
                    pnlBreadcrumbs.Children.Add(button);
                }
            }
        }

        private void Breadcrumb_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                NavigateToFolder(path);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(_currentViewFolder);
                NavigateToFolder(_backHistory.Pop(), addToHistory: false);
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(_currentViewFolder);
                NavigateToFolder(_forwardHistory.Pop(), addToHistory: false);
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentViewFolder) || _currentViewFolder == _currentRootFolder) return;

            var parent = Directory.GetParent(_currentViewFolder)?.FullName;
            if (!string.IsNullOrEmpty(parent) && parent.StartsWith(_currentRootFolder))
                NavigateToFolder(parent);
            else if (_currentViewFolder != _currentRootFolder)
                NavigateToFolder(_currentRootFolder);
        }

        private void DgFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedItem();
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
                    await OpenFolderAsync(dialog.SelectedPath);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ShowRefreshDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Load();
            var settingsWindow = new SettingsWindow(settings, _exportService, _databaseService, _currentRootFolder);
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                _settingsService.Save(settingsWindow.Settings);
            }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            PerformSearch();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Text = "";
            NavigateToFolder(_currentViewFolder, addToHistory: false);
            txtStatus.Text = "Search cleared";
        }

        private void TxtSearch_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                PerformSearch();
        }

        private async void PerformSearch()
        {
            if (_databaseService == null)
            {
                MessageBox.Show("Please select a folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                await RefreshCurrentFolderViewAsync();
                return;
            }

            var folder = _currentViewFolder ?? _currentRootFolder;
            List<FileEntry> results = await Task.Run(() => _databaseService.SearchFiles(query, folder));

            dgFiles.ItemsSource = results
                .Where(f => !f.Name.StartsWith(".") && !string.Equals(f.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Where(f =>
                {
                    try { return (File.GetAttributes(f.Path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                    catch { return false; }
                })
                .Where(MatchesFilter)
                .ToList();
            txtFileCount.Text = $"{results.Count} found";
            txtStatus.Text = $"Search results for: {query}";
        }

        private void DgFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedFile = dgFiles.SelectedItems.Count == 1 ? dgFiles.SelectedItem as FileEntry : null;
            if (dgFiles.SelectedItems.Count > 1)
            {
                ShowMultiSelectionPanel();
            }
            else
            {
                pnlMultiSelection.Visibility = Visibility.Collapsed;
                UpdateDetailsPanel();
                UpdatePreview(_selectedFile);
            }
        }

        private void ShowMultiSelectionPanel()
        {
            var selected = dgFiles.SelectedItems.Cast<FileEntry>().ToList();
            txtNoSelection.Visibility = Visibility.Collapsed;
            pnlFileDetails.Visibility = Visibility.Collapsed;
            pnlMultiSelection.Visibility = Visibility.Visible;

            var totalSize = selected.Where(f => !f.IsFolder).Sum(f => f.Size);
            var folderCount = selected.Count(f => f.IsFolder);
            var fileCount = selected.Count - folderCount;

            txtMultiCount.Text = $"{selected.Count} items selected ({folderCount} folders, {fileCount} files)";

            if (totalSize < 1024) txtMultiTotalSize.Text = $"Total size: {totalSize} B";
            else if (totalSize < 1024 * 1024) txtMultiTotalSize.Text = $"Total size: {totalSize / 1024.0:F1} KB";
            else if (totalSize < 1024 * 1024 * 1024) txtMultiTotalSize.Text = $"Total size: {totalSize / (1024.0 * 1024.0):F1} MB";
            else txtMultiTotalSize.Text = $"Total size: {totalSize / (1024.0 * 1024.0 * 1024.0):F2} GB";

            var typeCounts = selected.Where(f => !f.IsFolder).GroupBy(f => f.TypeDisplay)
                .OrderByDescending(g => g.Count()).Take(5)
                .Select(g => $"{g.Count()} {g.Key}");
            txtMultiTypes.Text = $"Types: {string.Join(", ", typeCounts)}";

            pnlPreview.Visibility = Visibility.Collapsed;
        }

        private void BtnBatchTags_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgFiles.SelectedItems.Cast<FileEntry>().Where(f => !f.IsFolder).ToList();
            if (selected.Count == 0 || _databaseService == null) return;

            var dialog = new RenameDialog("Enter tags (comma-separated)");
            dialog.Owner = this;
            dialog.Title = "Batch Assign Tags";
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
            {
                var tags = dialog.NewName.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                foreach (var file in selected)
                {
                    _databaseService.SetTagsForFile(file.Id, tags);
                    file.Tags = tags;
                    file.TagsText = string.Join(", ", tags);
                }
                txtStatus.Text = $"Tags assigned to {selected.Count} file(s)";
                RefreshCurrentFolderView();
            }
        }

        private void BtnBatchDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedFiles();
        }

        private void BtnBatchCopy_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedFiles(false);
        }

        private void UpdateDetailsPanel()
        {
            if (_selectedFile == null)
            {
                txtNoSelection.Visibility = Visibility.Visible;
                pnlFileDetails.Visibility = Visibility.Collapsed;
                txtNoSelection.Text = "No file selected";
                return;
            }

            if (_selectedFile.IsFolder)
            {
                txtNoSelection.Visibility = Visibility.Visible;
                pnlFileDetails.Visibility = Visibility.Collapsed;
                txtNoSelection.Text = $"Folder: {_selectedFile.Name}";
                return;
            }

            txtNoSelection.Visibility = Visibility.Collapsed;
            pnlFileDetails.Visibility = Visibility.Visible;

            txtFileName.Text = _selectedFile.Name;
            txtFilePath.Text = _selectedFile.Path;
            txtFileSize.Text = _selectedFile.SizeDisplay;
            txtFileCreated.Text = _selectedFile.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            txtFileModified.Text = _selectedFile.ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");
            txtNotes.Text = _selectedFile.Note ?? "";

            RebuildTagChips(_selectedFile.Tags);
            txtTagInput.Text = "";
        }

        private void RebuildTagChips(List<string> tags)
        {
            pnlTagChips.Children.Clear();
            if (tags == null) return;
            foreach (var tag in tags)
            {
                var chip = new TagChip
                {
                    TagName = tag,
                    ChipBackground = GetTagColor(tag)
                };
                chip.RemoveClicked += (s, e) =>
                {
                    pnlTagChips.Children.Remove(chip);
                    AutoSaveTags();
                };
                pnlTagChips.Children.Add(chip);
            }
        }

        private static readonly string[] TagColors = new[] { "#0078D4", "#107C10", "#D83B01", "#5C2D91", "#E81123", "#008272", "#E74856", "#8764B8", "#00B7C3", "#038387" };

        private System.Windows.Media.Brush GetTagColor(string tag)
        {
            var hash = Math.Abs(tag.GetHashCode());
            var colorHex = TagColors[hash % TagColors.Length];
            return new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
        }

        private List<string> GetCurrentTagsFromChips()
        {
            return pnlTagChips.Children.OfType<TagChip>().Select(c => c.TagName).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private void AutoSaveTags()
        {
            if (_selectedFile == null || _selectedFile.IsFolder || _databaseService == null) return;
            var tags = GetCurrentTagsFromChips();
            try
            {
                _databaseService.SetTagsForFile(_selectedFile.Id, tags);
                _databaseService.SetNoteForFile(_selectedFile.Id, txtNotes.Text);
                _selectedFile.Tags = tags;
                _selectedFile.TagsText = string.Join(", ", tags);
                _selectedFile.Note = txtNotes.Text;
                txtStatus.Text = "Saved";
            }
            catch (Exception ex)
            {
                LoggingService.Log($"AutoSaveTags failed: {ex.Message}");
            }
        }

        private void TxtTagInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTagFromInput();
                e.Handled = true;
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(txtTagInput.Text))
            {
                if (pnlTagChips.Children.Count > 0)
                {
                    pnlTagChips.Children.RemoveAt(pnlTagChips.Children.Count - 1);
                    AutoSaveTags();
                }
            }
            else if (e.Key == Key.Down)
            {
                if (tagAutocompleteList.Items.Count > 0 && tagAutocompletePopup.IsOpen)
                {
                    tagAutocompleteList.Focus();
                    tagAutocompleteList.SelectedIndex = 0;
                }
            }
        }

        private void AddTagFromInput()
        {
            var tag = txtTagInput.Text.Trim();
            if (string.IsNullOrEmpty(tag)) return;

            var existing = GetCurrentTagsFromChips();
            if (existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                txtTagInput.Text = "";
                return;
            }

            var chip = new TagChip
            {
                TagName = tag,
                ChipBackground = GetTagColor(tag)
            };
            chip.RemoveClicked += (s, e) =>
            {
                pnlTagChips.Children.Remove(chip);
                AutoSaveTags();
            };
            pnlTagChips.Children.Add(chip);
            txtTagInput.Text = "";
            tagAutocompletePopup.IsOpen = false;
            AutoSaveTags();
        }

        private void TagAutocompleteList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (tagAutocompleteList.SelectedItem is string tag)
            {
                txtTagInput.Text = tag;
                AddTagFromInput();
                txtTagInput.Focus();
            }
        }

        private void BtnSaveTags_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFile == null || _selectedFile.IsFolder || _databaseService == null) return;

            try
            {
                var tags = GetCurrentTagsFromChips();
                _databaseService.SetTagsForFile(_selectedFile.Id, tags);
                _databaseService.SetNoteForFile(_selectedFile.Id, txtNotes.Text);

                _selectedFile.Tags = tags;
                _selectedFile.TagsText = string.Join(", ", tags);
                _selectedFile.Note = txtNotes.Text;

                txtStatus.Text = "Saved";
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
                    using (var proc = Process.Start("explorer.exe", $"/select,\"{_selectedFile.Path}\"")) { }
                else
                    MessageBox.Show("Location not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    txtStatus.Text = $"Indexed {result.FilesIndexed} files in {result.Duration.TotalSeconds:F1}s";
                else if (result.Cancelled)
                    txtStatus.Text = "Cancelled";
                else
                    txtStatus.Text = $"Failed: {result.ErrorMessage}";

                if (result.Success)
                    ShowToast($"Indexed {result.FilesIndexed} files");
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

                GC.Collect();
                NavigateToFolder(_currentViewFolder, addToHistory: false);
            }
        }

        private void IndexerService_ProgressChanged(object sender, IndexProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = e.Percentage;
                txtProgressStatus.Text = $"Indexing: {e.Current}/{e.Total} ({e.Percentage:F0}%)";
            });
        }

        private void IndexerService_StatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() => txtProgressStatus.Text = status);
        }

        #endregion

        private async void ShowRefreshDialog()
        {
            var dlg = new RefreshDialog(_currentViewFolder);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                if (dlg.Choice == RefreshDialog.RefreshChoice.Entire)
                {
                    if (string.IsNullOrEmpty(_currentRootFolder))
                    {
                        MessageBox.Show("Please select a root folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    InitializeDatabaseForFolder(_currentRootFolder);
                    await StartIndexing();
                }
                else if (dlg.Choice == RefreshDialog.RefreshChoice.CurrentView)
                {
                    if (string.IsNullOrEmpty(_currentViewFolder))
                    {
                        MessageBox.Show("No current view folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    await RefreshSpecificFolderAsync(_currentViewFolder);
                }
                else if (dlg.Choice == RefreshDialog.RefreshChoice.SpecificFolder)
                {
                    var folder = dlg.SelectedFolder;
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        MessageBox.Show("Please choose a valid folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (string.IsNullOrEmpty(_currentRootFolder) || !folder.StartsWith(_currentRootFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        InitializeDatabaseForFolder(folder);
                        _currentRootFolder = folder;
                        _backHistory.Clear();
                        _forwardHistory.Clear();
                        btnRefresh.IsEnabled = true;
                        txtCurrentFolder.Text = folder;
                    }

                    var previousView = _currentViewFolder;
                    await RefreshSpecificFolderAsync(folder);
                    NavigateToFolder(previousView ?? folder, addToHistory: false);
                }
            }
        }

        private async Task RefreshSpecificFolderAsync(string folder)
        {
            if (_isIndexing) return;
            if (_indexerService == null || _databaseService == null)
            {
                InitializeDatabaseForFolder(_currentRootFolder ?? folder);
            }

            _isIndexing = true;
            _indexCancellation?.Dispose();
            _indexCancellation = new CancellationTokenSource();
            try
            {
                pnlProgress.Visibility = Visibility.Visible;
                progressBar.Value = 0;

                var result = await _indexerService.ScanFolderAsync(folder, _indexCancellation.Token);
                if (result.Success)
                    txtStatus.Text = $"Indexed {result.FilesIndexed} files in {result.Duration.TotalSeconds:F1}s";
                else if (result.Cancelled)
                    txtStatus.Text = "Cancelled";
                else
                    txtStatus.Text = $"Failed: {result.ErrorMessage}";

                NavigateToFolder(_currentViewFolder ?? folder, addToHistory: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error indexing folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isIndexing = false;
                pnlProgress.Visibility = Visibility.Collapsed;
                _indexCancellation?.Dispose();
                _indexCancellation = null;
            }
        }

        private void ShowLogViewer()
        {
            try
            {
                var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var viewerExe = Path.Combine(exeDir, "components", "Log_Viewer.exe");
                var logPath = LoggingService.LogFilePath;

                if (!File.Exists(viewerExe))
                {
                    MessageBox.Show("Log Viewer nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = viewerExe,
                    Arguments = $"--log \"{logPath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch
            {
                MessageBox.Show("Log Viewer konnte nicht gestartet werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleCheatSheet()
        {
            cheatSheetOverlay.Visibility = cheatSheetOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void CheatSheetOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource == cheatSheetOverlay)
            {
                cheatSheetOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private System.Threading.Timer _toastTimer;

        private void ShowToast(string message)
        {
            Dispatcher.Invoke(() =>
            {
                toastText.Text = message;
                toastNotification.Visibility = Visibility.Visible;
                _toastTimer?.Dispose();
                _toastTimer = new System.Threading.Timer(_ =>
                {
                    Dispatcher.Invoke(() => toastNotification.Visibility = Visibility.Collapsed);
                }, null, 3000, System.Threading.Timeout.Infinite);
            });
        }

        private void BtnToastClose_Click(object sender, RoutedEventArgs e)
        {
            toastNotification.Visibility = Visibility.Collapsed;
            _toastTimer?.Dispose();
        }

        private async Task OpenFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            var settingsSnapshot = _settingsService.Load();
            var persistence = settingsSnapshot.Persistence ?? new PersistenceOptions();

            _currentRootFolder = folderPath;

            string initialView = folderPath;
            if (persistence.LastViewFolder &&
                settingsSnapshot.FolderLastView != null &&
                settingsSnapshot.FolderLastView.TryGetValue(folderPath, out var lastView) &&
                !string.IsNullOrEmpty(lastView) &&
                Directory.Exists(lastView) &&
                lastView.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                initialView = lastView;
            }

            _currentViewFolder = initialView;
            _backHistory.Clear();
            _forwardHistory.Clear();

            InitializeDatabaseForFolder(folderPath);
            if (persistence.Filter)
                LoadFilterForFolder(folderPath);
            else
            {
                _currentFilter = "All Files";
                cmbFilter.SelectedIndex = 0;
            }

            if (persistence.ColumnVisibility)
                ApplySavedColumnVisibility(settingsSnapshot);

            btnRefresh.IsEnabled = true;
            txtCurrentFolder.Text = initialView;

            if (HasExistingIndex(folderPath) && _databaseService.GetFileCount() > 0)
            {
                var result = MessageBox.Show(
                    $"This folder already has an index with {_databaseService.GetFileCount()} files.\n\n" +
                    "Do you want to refresh the index?\n\nYes = Refresh | No = Use existing",
                    "Existing Index Found", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    await StartIndexing();
                else
                {
                    NavigateToFolder(initialView, addToHistory: false);
                    if (persistence.Sort)
                        ApplySavedSort(folderPath);
                    txtStatus.Text = "Loaded existing index";
                }
            }
            else
            {
                await StartIndexing();
                if (persistence.Sort)
                    ApplySavedSort(folderPath);
            }
        }

        private void RefreshCurrentFolderView()
        {
            _ = RefreshCurrentFolderViewAsync();
        }

        private async Task RefreshCurrentFolderViewAsync()
        {
            if (_databaseService == null || string.IsNullOrEmpty(_currentViewFolder))
            {
                dgFiles.ItemsSource = null;
                txtFileCount.Text = "0 items";
                return;
            }

            var items = new List<FileEntry>();
            var currentFolder = _currentViewFolder;

            HashSet<string> taggedDirectories = null;
            if (_currentFilter == "Tags")
            {
                taggedDirectories = await Task.Run(() => _databaseService.GetDirectoriesWithTaggedFiles(currentFolder));
            }

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
                if (taggedDirectories != null && !taggedDirectories.Contains(dir))
                    continue;

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
            dgFiles.ItemsSource = items;

            var folderCount = items.Count(i => i.IsFolder);
            var fileCount = items.Count(i => !i.IsFolder);
            txtFileCount.Text = $"{folderCount} folders, {fileCount} files";
        }

        private void OpenSelectedItem()
        {
            var selected = dgFiles.SelectedItem as FileEntry;
            if (selected == null) return;

            if (selected.IsFolder)
            {
                NavigateToFolder(selected.Path);
            }
            else
            {
                try
                {
                    using (var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = selected.Path,
                        UseShellExecute = true
                    })) { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task CopyOrMoveFilesAsync(string[] sourcePaths, string destinationFolder, bool move)
        {
            await Task.Run(() =>
            {
                var fullDestRoot = Path.GetFullPath(destinationFolder);
                foreach (var src in sourcePaths)
                {
                    try
                    {
                        var dest = Path.Combine(fullDestRoot, Path.GetFileName(src));
                        dest = GetUniquePath(dest);

                        if (!IsPathSafe(dest, fullDestRoot))
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                                txtStatus.Text = $"Skipped unsafe path: {Path.GetFileName(src)}"));
                            continue;
                        }

                        if (Directory.Exists(src))
                        {
                            CopyDirectoryRecursive(src, dest);
                            if (move)
                            {
                                Directory.Delete(src, true);
                                if (_databaseService != null)
                                    _databaseService.UpdateFolderPath(src, dest);
                            }
                        }
                        else if (File.Exists(src))
                        {
                            File.Copy(src, dest, overwrite: false);
                            if (move)
                            {
                                File.Delete(src);
                                if (_databaseService != null)
                                {
                                    var files = _databaseService.GetFilesInFolder(Path.GetDirectoryName(src));
                                    var dbFile = files.FirstOrDefault(f => f.Path == src);
                                    if (dbFile != null)
                                        _databaseService.UpdateFilePathAndName(dbFile.Id, dest, Path.GetFileName(dest));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                            txtStatus.Text = $"Error: {ex.Message}"));
                    }
                }
            });

            await RefreshCurrentFolderViewAsync();
            txtStatus.Text = move ? $"Moved {sourcePaths.Length} item(s)" : $"Copied {sourcePaths.Length} item(s)";
        }

        private static bool IsPathSafe(string filePath, string allowedRoot)
        {
            var fullPath = Path.GetFullPath(filePath);
            var fullRoot = Path.GetFullPath(allowedRoot);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullRoot += Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            // Do not follow symlinks/junctions — they could point outside the destination tree
            if ((File.GetAttributes(sourceDir) & FileAttributes.ReparsePoint) != 0)
                return;

            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var counter = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name} ({counter}){ext}");
                counter++;
            } while (File.Exists(newPath) || Directory.Exists(newPath));

            return newPath;
        }

        private static string GetUniqueFolderPath(string path)
        {
            if (!Directory.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            var counter = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{name} ({counter})");
                counter++;
            } while (Directory.Exists(newPath));

            return newPath;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                dropOverlay.Visibility = Visibility.Visible;
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

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;

            var firstPath = paths[0];
            if (Directory.Exists(firstPath))
            {
                await OpenFolderAsync(firstPath);
            }
        }

        private void DgFiles_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var direction = e.Column.SortDirection != ListSortDirection.Ascending
                ? ListSortDirection.Ascending : ListSortDirection.Descending;
            var path = e.Column.SortMemberPath;

            e.Column.SortDirection = direction;

            var view = CollectionViewSource.GetDefaultView(dgFiles.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();
            if (!string.IsNullOrEmpty(path))
            {
                view.SortDescriptions.Add(new SortDescription(path, direction));
            }

            foreach (var col in dgFiles.Columns)
            {
                if (col != e.Column)
                    col.SortDirection = null;
            }

            if (!string.IsNullOrEmpty(path))
                SaveSortForFolder(path, direction);
        }

        protected override void OnClosed(EventArgs e)
        {
            _indexCancellation?.Cancel();
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
            _toastTimer?.Dispose();
            _toastTimer = null;
            try { webPdfPreview?.Dispose(); } catch { }
            _databaseService?.Dispose();
            LoggingService.Shutdown();
            base.OnClosed(e);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [In] ref Guid riid, out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int SIIGBF_RESIZETOFIT = 0x00;
        private const int SIIGBF_THUMBNAILONLY = 0x08;

        private BitmapSource GetShellThumbnail(string path, int size)
        {
            try
            {
                var iid = ShellItemIID;
                var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                if (hr != 0 || factory == null) return null;

                var sz = new SIZE { cx = size, cy = size };
                factory.GetImage(sz, SIIGBF_RESIZETOFIT | SIIGBF_THUMBNAILONLY, out var hBitmap);
                if (hBitmap == IntPtr.Zero) return null;

                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size));
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}