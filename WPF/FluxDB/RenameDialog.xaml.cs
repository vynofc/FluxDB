using System.Windows;

namespace FluxDB
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; }

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            txtName.Text = currentName;
            txtName.SelectAll();
            txtName.Focus();
            Loaded += (s, e) => txtName.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            NewName = txtName.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}