namespace FluxDB.ViewModels
{
    public partial class NavigationViewModel : ObservableObject
    {
        private Stack<string> _backHistory = new();
        private Stack<string> _forwardHistory = new();
        private const int MaxHistorySize = 50;

        public NavigationViewModel()
        {
            LoggingService.Log("NavigationViewModel: constructor");
        }

        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoForward;

        [ObservableProperty]
        private bool _canGoUp;

        [ObservableProperty]
        private string _currentRootFolder;

        [ObservableProperty]
        private string _currentViewFolder;

        [ObservableProperty]
        private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();

        public void NavigateTo(string folderPath, bool addToHistory = true)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            if (addToHistory && !string.IsNullOrEmpty(CurrentViewFolder) && CurrentViewFolder != folderPath)
            {
                _backHistory.Push(CurrentViewFolder);
                if (_backHistory.Count > MaxHistorySize)
                {
                    var trimmed = _backHistory.ToArray();
                    _backHistory = new Stack<string>(trimmed.Take(MaxHistorySize));
                }
                _forwardHistory.Clear();
            }

            CurrentViewFolder = folderPath;
            UpdateNavigationState();
            UpdateBreadcrumbs();
        }

        [RelayCommand]
        private void GoBack()
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(CurrentViewFolder);
                CurrentViewFolder = _backHistory.Pop();
                UpdateNavigationState();
                UpdateBreadcrumbs();
                NotifyNavigated();
            }
        }

        [RelayCommand]
        private void GoForward()
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(CurrentViewFolder);
                CurrentViewFolder = _forwardHistory.Pop();
                UpdateNavigationState();
                UpdateBreadcrumbs();
                NotifyNavigated();
            }
        }

        [RelayCommand]
        private void GoUp()
        {
            if (string.IsNullOrEmpty(CurrentViewFolder) || CurrentViewFolder == CurrentRootFolder)
                return;

            var parent = Directory.GetParent(CurrentViewFolder)?.FullName;
            if (parent != null)
            {
                NavigateTo(parent);
                NotifyNavigated();
            }
        }

        public event Action Navigated;

        private void NotifyNavigated()
        {
            Navigated?.Invoke();
        }

        public void SetRootFolder(string rootFolder)
        {
            CurrentRootFolder = rootFolder;
            CurrentViewFolder = rootFolder;
            _backHistory.Clear();
            _forwardHistory.Clear();
            UpdateNavigationState();
            UpdateBreadcrumbs();
        }

        private void UpdateNavigationState()
        {
            CanGoBack = _backHistory.Count > 0;
            CanGoForward = _forwardHistory.Count > 0;
            CanGoUp = !string.IsNullOrEmpty(CurrentViewFolder) &&
                      CurrentViewFolder != CurrentRootFolder &&
                      Directory.GetParent(CurrentViewFolder)?.FullName != null;
        }

        private void UpdateBreadcrumbs()
        {
            Breadcrumbs.Clear();
            if (string.IsNullOrEmpty(CurrentViewFolder) || string.IsNullOrEmpty(CurrentRootFolder))
                return;

            var relativePath = CurrentViewFolder;
            if (CurrentViewFolder.StartsWith(CurrentRootFolder))
            {
                relativePath = CurrentViewFolder.Substring(CurrentRootFolder.Length).TrimStart('\\');
            }

            Breadcrumbs.Add(new BreadcrumbItem
            {
                Name = Path.GetFileName(CurrentRootFolder),
                Path = CurrentRootFolder
            });

            if (!string.IsNullOrEmpty(relativePath))
            {
                var parts = relativePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var currentPath = CurrentRootFolder;
                foreach (var part in parts)
                {
                    currentPath = Path.Combine(currentPath, part);
                    Breadcrumbs.Add(new BreadcrumbItem { Name = part, Path = currentPath });
                }
            }
        }
    }

    public class BreadcrumbItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }
}