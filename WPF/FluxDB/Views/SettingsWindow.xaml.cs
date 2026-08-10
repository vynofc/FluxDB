using System;
using System.Windows;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace FluxDB.Views
{
    public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
    {
        public AppSettings Settings { get; private set; }
        private readonly ExportService _exportService;
        private readonly DatabaseService _databaseService;
        private readonly string _rootFolder;

        public SettingsWindow(AppSettings settings, ExportService exportService, DatabaseService databaseService, string rootFolder)
        {
            InitializeComponent();

            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
            ExtendsContentIntoTitleBar = true;
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round;

            Settings = settings ?? new AppSettings();
            _exportService = exportService;
            _databaseService = databaseService;
            _rootFolder = rootFolder;

            LoadSettings();
            UpdateUpdateStatus();
        }

        private void LoadSettings()
        {
            txtCurrentVersion.Text = App.GetLocalVersion();
            chkAutoUpdate.IsChecked = Settings.AutoUpdateCheck;

            cmbTheme.Items.Add("Dark");
            cmbTheme.Items.Add("Light");
            cmbTheme.Items.Add("High Contrast");
            cmbTheme.SelectedItem = Settings.Theme ?? "Dark";

            var p = Settings.Persistence ?? new PersistenceOptions();
            chkPersistLastRootFolder.IsChecked = p.LastRootFolder;
            chkPersistLastViewFolder.IsChecked = p.LastViewFolder;
            chkPersistFilter.IsChecked = p.Filter;
            chkPersistSort.IsChecked = p.Sort;
            chkPersistColumnVisibility.IsChecked = p.ColumnVisibility;
            chkPersistRecentFolders.IsChecked = p.RecentFolders;
        }

        private void UpdateUpdateStatus()
        {
            if (App.IsUpdateAvailable)
            {
                if (App.IsBetaUpdate)
                {
                    txtUpdateStatus.Text = $"Beta available: {App.AvailableVersion}";
                    txtUpdateStatus.Foreground = Brushes.Orange;
                    txtBetaWarning.Visibility = Visibility.Visible;
                    btnDownloadUpdate.Visibility = Visibility.Visible;
                }
                else
                {
                    txtUpdateStatus.Text = $"Update available: {App.AvailableVersion}";
                    txtUpdateStatus.Foreground = Brushes.Orange;
                    btnDownloadUpdate.Visibility = Visibility.Visible;
                }

                if (App.AvailableBetaVersion != null && !App.IsBetaUpdate)
                {
                    txtUpdateDetails.Text = $"Also: Beta {App.AvailableBetaVersion} available";
                    txtUpdateDetails.Visibility = Visibility.Visible;
                }
            }
            else
            {
                txtUpdateStatus.Text = "Your version is up to date.";
                txtUpdateStatus.Foreground = Brushes.LightGreen;
                btnDownloadUpdate.Visibility = Visibility.Collapsed;
            }

            if (App.IsUpdateSkipped)
            {
                txtUpdateDetails.Text = "Program started with --noupdate. Auto-update was skipped.";
                txtUpdateDetails.Visibility = Visibility.Visible;
            }
            else if (App.AvailableBetaVersion == null || App.IsBetaUpdate)
            {
                txtUpdateDetails.Visibility = Visibility.Collapsed;
            }
        }

        private void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbTheme.SelectedItem is string theme)
            {
                Settings.Theme = theme;
                if (theme == "Light")
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                else if (theme == "High Contrast")
                    ApplicationThemeManager.Apply(ApplicationTheme.HighContrast);
                else
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Settings.AutoUpdateCheck = chkAutoUpdate.IsChecked ?? false;
            Settings.Theme = cmbTheme.SelectedItem as string ?? "Dark";

            if (Settings.Persistence == null)
                Settings.Persistence = new PersistenceOptions();

            Settings.Persistence.LastRootFolder = chkPersistLastRootFolder.IsChecked ?? true;
            Settings.Persistence.LastViewFolder = chkPersistLastViewFolder.IsChecked ?? true;
            Settings.Persistence.Filter = chkPersistFilter.IsChecked ?? true;
            Settings.Persistence.Sort = chkPersistSort.IsChecked ?? true;
            Settings.Persistence.ColumnVisibility = chkPersistColumnVisibility.IsChecked ?? true;
            Settings.Persistence.RecentFolders = chkPersistRecentFolders.IsChecked ?? true;

            DialogResult = true;
            Close();
        }

        private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                btnDownloadUpdate.IsEnabled = false;
                btnDownloadUpdate.Content = "Downloading...";

                var tag = App.AvailableTag;
                if (string.IsNullOrEmpty(tag))
                {
                    var latestTag = await FetchLatestReleaseTagAsync();
                    if (latestTag == null)
                    {
                        MessageBox.Show("Could not fetch latest release information.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    tag = latestTag;
                }

                var assembly = Assembly.GetExecutingAssembly();
                var exeDir = Path.GetDirectoryName(assembly.Location) ?? ".";
                var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");

                if (!File.Exists(installerPath))
                {
                    var downloaded = await DownloadInstallerAsync(exeDir, tag);
                    if (!downloaded)
                    {
                        MessageBox.Show("Failed to download installer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                btnDownloadUpdate.Content = "Starting installer...";

                var args = "--silent-start";
                if (App.IsBetaUpdate)
                {
                    args += " --beta";
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo(installerPath, args)
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = true
                };
                using (var proc = System.Diagnostics.Process.Start(startInfo)) { }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LoggingService.Log($"BtnDownloadUpdate_Click failed: {ex.Message}");
                MessageBox.Show("Update failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btnDownloadUpdate.IsEnabled = true;
                btnDownloadUpdate.Content = "Update Now";
            }
        }

        private async Task<string> FetchLatestReleaseTagAsync()
        {
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxDB");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                    var response = await http.GetAsync("https://api.github.com/repos/vynofc/FluxDB/releases/latest");
                    if (!response.IsSuccessStatusCode) return null;

                    var json = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    return release?.TagName;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"FetchLatestReleaseTagAsync failed: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> DownloadInstallerAsync(string exeDir, string tag)
        {
            try
            {
                var url = $"https://github.com/vynofc/FluxDB/releases/download/{Uri.EscapeDataString(tag)}/FluxDB-Installer.exe";
                var destPath = Path.Combine(exeDir, "FluxDB-Installer.exe");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    var bytes = await http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(destPath, bytes);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DownloadInstallerAsync failed: {ex.Message}");
                return false;
            }
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
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
                    var fileName = dialog.FileName;
                    var rootFolder = _rootFolder;
                    await Task.Run(() =>
                    {
                        if (fileName.EndsWith(".gz"))
                            _exportService.ExportToGzip(fileName, rootFolder);
                        else
                            _exportService.ExportToJson(fileName, rootFolder);
                    });

                    GC.Collect();
                    MessageBox.Show($"Export complete:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_databaseService == null)
            {
                MessageBox.Show("Please select a folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json;*.json.gz)|*.json;*.json.gz|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = "index.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var fileName = dialog.FileName;
                    var rootFolder = _rootFolder;
                    var importService = new ImportService(_databaseService);
                    await Task.Run(() =>
                    {
                        importService.ImportFromFile(fileName, rootFolder);
                    });

                    GC.Collect();
                    MessageBox.Show($"Import complete:\n{dialog.FileName}", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}