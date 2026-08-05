namespace FluxDB.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private ObservableCollection<string> _recentFolders = new();

        public DashboardViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
            LoadRecentFolders();
        }

        private void LoadRecentFolders()
        {
            var settings = _settingsService.Load();
            if (settings.RecentFolders != null)
            {
                RecentFolders = new ObservableCollection<string>(settings.RecentFolders);
            }
        }

        [RelayCommand]
        private void OpenFolder(string folderPath)
        {
            WeakReferenceMessenger.Default.Send(new FolderOpenedMessage(folderPath));
        }
    }

    public class FolderOpenedMessage
    {
        public string FolderPath { get; }
        public FolderOpenedMessage(string folderPath) => FolderPath = folderPath;
    }
}