using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;
using FluxDB.Plugin;

namespace FluxDB
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }
        private readonly ExportService _exportService;
        private readonly string _rootFolder;
        private List<PluginViewModel> _pluginViewModels;

        public SettingsWindow(AppSettings settings, ExportService exportService, string rootFolder)
        {
            InitializeComponent();
            Settings = settings ?? new AppSettings();
            _exportService = exportService;
            _rootFolder = rootFolder;

            LoadSettings();
            LoadPlugins();
            UpdateUpdateStatus();
        }

        private void LoadSettings()
        {
            txtCurrentVersion.Text = App.GetLocalVersion();
            chkAutoUpdate.IsChecked = Settings.AutoUpdateCheck;
        }

        private void LoadPlugins()
        {
            var plugins = PluginService.Plugins;
            var disabledSet = Settings.DisabledPlugins ?? new List<string>();

            _pluginViewModels = plugins.Select(p => new PluginViewModel
            {
                Name = p.Name ?? "Unknown",
                Version = p.Version ?? "1.0",
                Author = p.Author ?? "",
                Description = p.Description ?? "",
                Status = p.Status,
                StatusText = p.Status == PluginStatus.Loaded ? "Loaded" :
                             p.Status == PluginStatus.Failed ? "Failed" : "Disabled",
                IsEnabled = !disabledSet.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                          && p.Status == PluginStatus.Loaded
            }).ToList();

            pluginList.ItemsSource = _pluginViewModels;
            txtNoPlugins.Visibility = _pluginViewModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PluginCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;
            var vm = checkBox.DataContext as PluginViewModel;
            if (vm == null) return;

            if (Settings.DisabledPlugins == null)
                Settings.DisabledPlugins = new List<string>();

            if (vm.IsEnabled)
                Settings.DisabledPlugins.RemoveAll(p => string.Equals(p, vm.Name, StringComparison.OrdinalIgnoreCase));
            else if (!Settings.DisabledPlugins.Contains(vm.Name, StringComparer.OrdinalIgnoreCase))
                Settings.DisabledPlugins.Add(vm.Name);
        }

        private void BtnOpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FluxDB", "Plugins");
                if (!Directory.Exists(appData))
                    Directory.CreateDirectory(appData);
                Process.Start("explorer.exe", appData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open plugins folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

    public class PluginViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public PluginStatus Status { get; set; }
        public string StatusText { get; set; }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}