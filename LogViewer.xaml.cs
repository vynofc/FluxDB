using System.Windows;
using FluxDB.Services;

namespace FluxDB
{
    public partial class LogViewer : Window
    {
        public LogViewer()
        {
            InitializeComponent();
            LoadLogs();
        }

        private void LoadLogs()
        {
            var lines = LoggingService.GetLogs();
            txtLogs.Text = string.Join("\n", lines);
            txtLogs.ScrollToEnd();
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Clear logs? This will remove current in-memory and file logs.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                LoggingService.Clear();
                LoadLogs();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
