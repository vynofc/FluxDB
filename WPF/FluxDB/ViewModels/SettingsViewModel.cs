namespace FluxDB.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly ExportService _exportService;
        private readonly DatabaseService _databaseService;
        private readonly ImportService _importService;

        [ObservableProperty]
        private string _currentVersion;

        [ObservableProperty]
        private bool _autoUpdateCheck;

        [ObservableProperty]
        private string _rootFolder;

        [ObservableProperty]
        private string _updateStatus = "Checking...";

        [ObservableProperty]
        private bool _isUpdateAvailable;

        [ObservableProperty]
        private string _availableVersion;

        [ObservableProperty]
        private bool _isBetaUpdate;

        [ObservableProperty]
        private string _betaWarning = "";

        public SettingsViewModel(SettingsService settingsService, ExportService exportService,
            DatabaseService databaseService, ImportService importService)
        {
            _settingsService = settingsService;
            _exportService = exportService;
            _databaseService = databaseService;
            _importService = importService;

            CurrentVersion = App.GetLocalVersion();
            var settings = _settingsService.Load();
            AutoUpdateCheck = settings.AutoUpdateCheck;
            RootFolder = settings.LastRootFolder ?? "";

            IsUpdateAvailable = App.IsUpdateAvailable;
            AvailableVersion = App.AvailableVersion;
            IsBetaUpdate = App.IsBetaUpdate;
            BetaWarning = IsBetaUpdate ? "⚠ This is a beta update!" : "";
            UpdateStatus = IsUpdateAvailable
                ? $"Update available: {AvailableVersion}"
                : "Your version is up to date.";
        }

        [RelayCommand]
        private void Save()
        {
            var settings = _settingsService.Load();
            settings.AutoUpdateCheck = AutoUpdateCheck;
            _settingsService.Save(settings);
        }

        [RelayCommand]
        private void DownloadUpdate()
        {
            var url = App.IsBetaUpdate && !string.IsNullOrEmpty(App.AvailableTag)
                ? $"https://github.com/vynofc/FluxDB/releases/tag/{App.AvailableTag}"
                : "https://github.com/vynofc/FluxDB/releases/latest";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        [RelayCommand]
        private async Task Export()
        {
            if (_exportService == null)
            {
                System.Windows.MessageBox.Show("Please select a folder first.", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
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
                    var root = RootFolder;
                    await Task.Run(() =>
                    {
                        if (fileName.EndsWith(".gz"))
                            _exportService.ExportToGzip(fileName, root);
                        else
                            _exportService.ExportToJson(fileName, root);
                    });
                    System.Windows.MessageBox.Show($"Export complete:\n{fileName}", "Export Complete",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private async Task Import()
        {
            if (_databaseService == null)
            {
                System.Windows.MessageBox.Show("Please select a folder first.", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
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
                    var root = RootFolder;
                    await Task.Run(() => _importService.ImportFromFile(fileName, root));
                    System.Windows.MessageBox.Show($"Import complete:\n{fileName}", "Import Complete",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Import failed: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }
}