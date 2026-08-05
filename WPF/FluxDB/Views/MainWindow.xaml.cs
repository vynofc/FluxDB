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
            InitializeComponent();
            _viewModel = viewModel;
            _settingsService = settingsService;

            DataContext = viewModel;

            WindowBackdropType = WindowBackdropType.Mica;
            ExtendsContentIntoTitleBar = true;
            WindowCornerPreference = WindowCornerPreference.Round;

            Loaded += OnLoaded;
            Drop += Window_Drop;
            DragOver += Window_DragOver;

            // Set icons via code-behind
            btnBack.Icon = new SymbolIcon(SymbolRegular.ArrowLeft24);
            btnForward.Icon = new SymbolIcon(SymbolRegular.ArrowRight24);
            btnUp.Icon = new SymbolIcon(SymbolRegular.ArrowUp24);
            btnOpenFolder.Icon = new SymbolIcon(SymbolRegular.FolderOpen24);
            btnRefresh.Icon = new SymbolIcon(SymbolRegular.ArrowSync24);
            btnSettings.Icon = new SymbolIcon(SymbolRegular.Settings24);
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
                case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                    txtSearch.Focus();
                    e.Handled = true;
                    break;
                case Key.Left when Keyboard.Modifiers == ModifierKeys.Alt:
                    _viewModel.Navigation.GoBack();
                    e.Handled = true;
                    break;
                case Key.Right when Keyboard.Modifiers == ModifierKeys.Alt:
                    _viewModel.Navigation.GoForward();
                    e.Handled = true;
                    break;
                case Key.Up when Keyboard.Modifiers == ModifierKeys.Alt:
                    _viewModel.Navigation.GoUp();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _viewModel.OpenItemCommand.Execute(_viewModel.SelectedFile);
                    e.Handled = true;
                    break;
                case Key.Back:
                    _viewModel.Navigation.GoUp();
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