using System.Windows.Input;
using Wpf.Ui.Controls;

namespace FluxDB.Views.Pages
{
    public partial class FileBrowserPage : System.Windows.Controls.Page
    {
        private readonly MainViewModel _viewModel;

        public FileBrowserPage(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            btnBack.Icon = new SymbolIcon(SymbolRegular.ArrowLeft24);
            btnForward.Icon = new SymbolIcon(SymbolRegular.ArrowRight24);
            btnUp.Icon = new SymbolIcon(SymbolRegular.ArrowUp24);
            btnOpenFolder.Icon = new SymbolIcon(SymbolRegular.FolderOpen24);
            btnRefresh.Icon = new SymbolIcon(SymbolRegular.ArrowSync24);
        }

        private void DgFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _viewModel.OpenItemCommand.Execute(_viewModel.SelectedFile);
        }
    }
}