using System.Windows;
using System.Windows.Forms;

namespace FluxDB
{
    public partial class RefreshDialog : Window
    {
        public enum RefreshChoice
        {
            Entire,
            CurrentView,
            SpecificFolder
        }

        public RefreshChoice Choice { get; private set; } = RefreshChoice.Entire;
        public string SelectedFolder { get; private set; }

        public RefreshDialog(string currentViewFolder)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(currentViewFolder))
            {
                rbView.IsEnabled = true;
            }
            else
            {
                rbView.IsEnabled = false;
            }
            txtSelectedFolder.Text = "";
            rbEntire.IsChecked = true;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder to refresh";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtSelectedFolder.Text = dlg.SelectedPath;
                    rbSelect.IsChecked = true;
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (rbEntire.IsChecked == true)
                Choice = RefreshChoice.Entire;
            else if (rbView.IsChecked == true)
                Choice = RefreshChoice.CurrentView;
            else
                Choice = RefreshChoice.SpecificFolder;

            SelectedFolder = txtSelectedFolder.Text;
            DialogResult = true;
            Close();
        }
    }
}