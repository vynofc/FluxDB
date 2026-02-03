using System;
using System.Windows;
using System.Windows.Media;
using FluxDB.Services;

namespace FluxDB
{
    /// <summary>
    /// Interaktionslogik für LicenseWindow.xaml
    /// </summary>
    public partial class LicenseWindow : Window
    {
        private readonly LicenseService _licenseService;
        private readonly ExportService _exportService;
        private readonly string _rootFolder;

        public LicenseWindow(LicenseService licenseService, ExportService exportService, string rootFolder)
        {
            InitializeComponent();
            _licenseService = licenseService;
            _exportService = exportService;
            _rootFolder = rootFolder;

            LoadCurrentLicenseInfo();
        }

        private void LoadCurrentLicenseInfo()
        {
            txtDeviceId.Text = _licenseService.GetDeviceId();
            txtLicenseKey.Text = _licenseService.GetStoredLicenseKey() ?? "";

            UpdateLicenseStatusDisplay();
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
                txtLicenseStatus.Text = "? Active";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4caf50"));
            }
            else
            {
                txtLicenseStatus.Text = "? Invalid";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f44336"));
            }

            txtLicenseExpires.Text = license.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
            txtLastCheck.Text = license.LastChecked.HasValue ? license.LastChecked.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            if (!string.IsNullOrEmpty(license.ErrorMessage))
            {
                txtLicenseStatus.Text += $" ({license.ErrorMessage})";
            }
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
    }
}
