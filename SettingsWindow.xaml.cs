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
        private readonly LicenseService _licenseService;
        private readonly ExportService _exportService;
        private readonly string _rootFolder;

        public SettingsWindow(AppSettings settings, LicenseService licenseService, ExportService exportService, string rootFolder)
        {
            InitializeComponent();
            Settings = settings ?? new AppSettings();
            _licenseService = licenseService;
            _exportService = exportService;
            _rootFolder = rootFolder;

            LoadSettings();
            LoadLicenseInfo();
            UpdateUpdateStatus();
        }

        private void LoadSettings()
        {
            txtCurrentVersion.Text = App.GetLocalVersion();
            cmbTheme.SelectedIndex = Settings.Theme == "Light" ? 1 : 0;
            txtPreviewScale.Text = Settings.PreviewScale.ToString("0.0");
        }

        private void LoadLicenseInfo()
        {
            txtDeviceId.Text = _licenseService.GetDeviceId();
            txtLicenseKey.Text = _licenseService.GetStoredLicenseKey() ?? "";
            UpdateLicenseStatusDisplay();
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

        private async void UpdateLicenseStatusDisplay()
        {
            var storedKey = _licenseService.GetStoredLicenseKey();
            if (string.IsNullOrEmpty(storedKey))
            {
                txtLicenseStatus.Text = "Not activated";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff9800"));
                txtLicenseExpires.Text = "-";
                txtLastCheck.Text = "-";
                return;
            }

            var license = await _licenseService.CheckLicenseAsync(storedKey);

            if (license.Valid)
            {
                txtLicenseStatus.Text = "✓ Active";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4caf50"));
            }
            else
            {
                txtLicenseStatus.Text = "✗ Invalid";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f44336"));
            }

            txtLicenseExpires.Text = license.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
            txtLastCheck.Text = license.LastChecked.HasValue ? license.LastChecked.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            if (!string.IsNullOrEmpty(license.ErrorMessage))
            {
                txtLicenseStatus.Text += $" ({license.ErrorMessage})";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var themeItem = cmbTheme.SelectedItem as ComboBoxItem;
            Settings.Theme = themeItem?.Content?.ToString() ?? "Dark";

            if (double.TryParse(txtPreviewScale.Text, out var s))
            {
                Settings.PreviewScale = Math.Max(0.3, Math.Min(3.0, s));
            }

            DialogResult = true;
            Close();
        }

        private void BtnCopyDeviceId_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtDeviceId.Text);
            MessageBox.Show("Device ID copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            var licenseKey = txtLicenseKey.Text.Trim();
            if (string.IsNullOrEmpty(licenseKey))
            {
                MessageBox.Show("Please enter a license key.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnActivate.IsEnabled = false;
            btnActivate.Content = "Checking...";

            try
            {
                var license = await _licenseService.CheckLicenseAsync(licenseKey, forceRefresh: true);

                if (license.Valid)
                {
                    MessageBox.Show("License activated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"License validation failed.\n{license.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                UpdateLicenseStatusDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking license: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnActivate.IsEnabled = true;
                btnActivate.Content = "Activate License";
            }
        }

        private void BtnClearLicense_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the license?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _licenseService.ClearLicense();
                txtLicenseKey.Text = "";
                UpdateLicenseStatusDisplay();
                MessageBox.Show("License cleared.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://nsce-cdn.fun/FluxDB/FluxDB-Installer.exe");
        }
    }
}
