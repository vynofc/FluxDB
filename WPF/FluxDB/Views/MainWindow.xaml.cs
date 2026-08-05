using System.Reflection;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace FluxDB.Views
{
    public partial class MainWindow : FluentWindow
    {
        private readonly MainViewModel _viewModel;
        private readonly SettingsService _settingsService;

        public MainWindow(MainViewModel viewModel, SettingsService settingsService)
        {
            try
            {
                LoggingService.Log("MainWindow: constructor start");
                _viewModel = viewModel;
                _settingsService = settingsService;

                DataContext = viewModel;
                LoggingService.Log("MainWindow: DataContext set, calling InitializeComponent");
                InitializeComponent();
                LoggingService.Log("MainWindow: InitializeComponent done");

                WindowBackdropType = WindowBackdropType.Mica;
                ExtendsContentIntoTitleBar = true;
                WindowCornerPreference = WindowCornerPreference.Round;

                Loaded += OnLoaded;
                Drop += Window_Drop;
                DragOver += Window_DragOver;

                LoggingService.Log("MainWindow: setting button icons");
                btnBack.Icon = new SymbolIcon(SymbolRegular.ArrowLeft24);
                btnForward.Icon = new SymbolIcon(SymbolRegular.ArrowRight24);
                btnUp.Icon = new SymbolIcon(SymbolRegular.ArrowUp24);
                btnOpenFolder.Icon = new SymbolIcon(SymbolRegular.FolderOpen24);
                btnRefresh.Icon = new SymbolIcon(SymbolRegular.ArrowSync24);
                btnNewFolder.Icon = new SymbolIcon(SymbolRegular.Folder24);
                btnSettings.Icon = new SymbolIcon(SymbolRegular.Settings24);
                LoggingService.Log("MainWindow: constructor complete");
            }
            catch (Exception ex)
            {
                LoggingService.Log($"MainWindow constructor CRASH: {ex}");
                throw;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var icoPath = Path.Combine(exeDir ?? string.Empty, "FluxDB-icon.ico");
                if (File.Exists(icoPath))
                {
                    Icon = BitmapFrame.Create(new Uri(icoPath));
                }
            }
            catch (Exception ex) { LoggingService.Log($"Failed to load window icon: {ex.Message}"); }

            await _viewModel.LoadInitialData();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox)
            {
                if (e.Key == Key.Escape)
                {
                    dgFiles.Focus();
                    e.Handled = true;
                }
                return;
            }

            var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
            var shift = Keyboard.Modifiers == ModifierKeys.Shift;
            var alt = Keyboard.Modifiers == ModifierKeys.Alt;

            switch (e.Key)
            {
                case Key.F5:
                    _viewModel.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.F2:
                    _viewModel.RenameCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    _viewModel.DeleteCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.F when ctrl:
                    txtSearch.Focus();
                    e.Handled = true;
                    break;
                case Key.C when ctrl && !shift:
                    _viewModel.CopyCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.X when ctrl:
                    _viewModel.CutCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.V when ctrl:
                    _viewModel.PasteCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.N when ctrl:
                    _viewModel.NewFolderCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Left when alt:
                    _viewModel.Navigation.GoBackCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Right when alt:
                    _viewModel.Navigation.GoForwardCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Up when alt:
                    _viewModel.Navigation.GoUpCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _viewModel.OpenItemCommand.Execute(_viewModel.SelectedFile);
                    e.Handled = true;
                    break;
                case Key.Back:
                    _viewModel.Navigation.GoUpCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        private void DgFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _viewModel.OpenItemCommand.Execute(_viewModel.SelectedFile);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = App.Host.Services.GetRequiredService<SettingsWindow>();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void TxtSearch_QuerySubmitted(object sender, object e)
        {
            _viewModel.SearchCommand.Execute(null);
        }

        private void CmbFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbFilter.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                _viewModel.FilterChangedCommand.Execute(item.Content?.ToString());
            }
        }

        private void Breadcrumb_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BreadcrumbItem item)
            {
                _viewModel.NavigateToBreadcrumbCommand.Execute(item.Path);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;

            var firstPath = paths[0];
            if (Directory.Exists(firstPath))
            {
                await _viewModel.OpenFolderAsync(firstPath);
            }
        }
    }
}