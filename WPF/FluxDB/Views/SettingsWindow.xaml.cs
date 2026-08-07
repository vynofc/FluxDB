using System;
using System.Windows;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
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
        }

        private void UpdateUpdateStatus()
        {
            if (App.IsUpdateAvailable)
            {
                txtUpdateStatus.Text = $"Update available: {App.AvailableVersion}";
                txtUpdateStatus.Foreground = Brushes.Orange;
                btnDownloadUpdate.Visibility = Visibility.Visible;
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
            else
            {
                txtUpdateDetails.Visibility = Visibility.Collapsed;
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
            DialogResult = true;
            Close();
        }

        private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/vynofc/FluxDB/releases/latest");
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