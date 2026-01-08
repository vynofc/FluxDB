using System;
using System.Windows;
using System.Windows.Controls;
using FluxDB.Models;

namespace FluxDB
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            Settings = settings ?? new AppSettings();

            // Initialize controls using FindName to avoid generated field issues
            var cmb = FindName("cmbTheme") as ComboBox;
            if (cmb != null)
            {
                cmb.SelectedIndex = Settings.Theme == "Light" ? 1 : 0;
            }

            var txt = FindName("txtPreviewScale") as TextBox;
            if (txt != null)
            {
                txt.Text = Settings.PreviewScale.ToString("0.0");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var cmb = FindName("cmbTheme") as ComboBox;
            if (cmb != null)
            {
                var themeItem = cmb.SelectedItem as ComboBoxItem;
                Settings.Theme = themeItem?.Content?.ToString() ?? "Dark";
            }

            var txt = FindName("txtPreviewScale") as TextBox;
            if (txt != null && double.TryParse(txt.Text, out var s))
            {
                Settings.PreviewScale = Math.Max(0.3, Math.Min(3.0, s));
            }

            DialogResult = true;
            Close();
        }
    }
}
