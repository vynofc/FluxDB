using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;

namespace FluxDB
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }
        private readonly ExportService _exportService;
        private readonly string _rootFolder;

        public SettingsWindow(AppSettings settings, ExportService exportService, string rootFolder)
        {
            InitializeComponent();
            Settings = settings ?? new AppSettings();
            _exportService = exportService;
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
            else if (App.IsBetaUpdateAvailable)
            {
                txtUpdateStatus.Text = $"Beta available: {App.AvailableBetaVersion}";
                txtUpdateStatus.Foreground = Brushes.Cyan;
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
            else if (App.IsBetaUpdateAvailable && !App.IsUpdateAvailable)
            {
                txtUpdateDetails.Text = "A newer beta version is available for testing.";
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
            System.Diagnostics.Process.Start("https://nsce-cdn.fun/FluxDB/FluxDB-Installer.exe");
        }
    }
}