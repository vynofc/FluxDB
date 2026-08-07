namespace FluxDB.Views.Pages
{
    public partial class DashboardPage : System.Windows.Controls.Page
    {
        public DashboardPage(DashboardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}