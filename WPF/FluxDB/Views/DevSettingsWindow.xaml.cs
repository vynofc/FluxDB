using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluxDB.Models;
using FluxDB.Services;

namespace FluxDB.Views
{
    public partial class DevSettingsWindow : Wpf.Ui.Controls.FluentWindow
    {
        private const string DevSettingsPassword = "spiderman007";

        private readonly SettingsService _settingsService;
        private AppSettings _settings;
        private readonly Dictionary<string, TextBox> _editors = new Dictionary<string, TextBox>();
        private bool _unlocked;

        public DevSettingsWindow(SettingsService settingsService)
        {
            InitializeComponent();

            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
            ExtendsContentIntoTitleBar = true;
            WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.Round;

            _settingsService = settingsService ?? new SettingsService();

            Loaded += (s, e) => pwdDevSettings.Focus();
        }

        private void Unlock()
        {
            if (_unlocked) return;

            if (pwdDevSettings.Password != DevSettingsPassword)
            {
                txtPasswordError.Visibility = Visibility.Visible;
                pwdDevSettings.SelectAll();
                pwdDevSettings.Focus();
                return;
            }

            _unlocked = true;
            pnlPassword.Visibility = Visibility.Collapsed;
            pnlHeader.Visibility = Visibility.Visible;
            tabContent.Visibility = Visibility.Visible;
            pnlFooter.Visibility = Visibility.Visible;

            _settings = _settingsService.Load();

            txtDeviceId.Text = DeviceIdentityService.GetOrCreateDeviceId();
            BuildInfoRows();

            BuildEditorRows();
        }

        private void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            Unlock();
        }

        private void PwdDevSettings_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Unlock();
                e.Handled = true;
            }
        }

        private void BuildInfoRows()
        {
            AddInfoRow(pnlSystemInfo, "Version", App.GetLocalVersion());
            AddInfoRow(pnlSystemInfo, "Debug-Modus", LoggingService.IsDebugMode ? "Aktiv" : "Inaktiv");
            AddInfoRow(pnlSystemInfo, "Betriebssystem", Environment.OSVersion.ToString());
            AddInfoRow(pnlSystemInfo, ".NET Runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            AddInfoRow(pnlSystemInfo, "Architektur", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
            AddInfoRow(pnlSystemInfo, "Windows-Benutzer", Environment.UserName);

            AddInfoRow(pnlPathInfo, "App-Verzeichnis", AppDomain.CurrentDomain.BaseDirectory);
            AddInfoRow(pnlPathInfo, "AppData", _settingsService.GetAppDataDirectory());
            AddInfoRow(pnlPathInfo, "Log-Datei", LoggingService.LogFilePath);
        }

        private void AddInfoRow(StackPanel panel, string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBox = new TextBox
            {
                Text = value ?? "",
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueBox, 1);
            grid.Children.Add(valueBox);

            panel.Children.Add(grid);
        }

        private void BtnCopyDeviceId_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtDeviceId.Text);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DevSettingsWindow.BtnCopyDeviceId_Click failed: {ex.Message}");
            }
        }

        private void BtnResetDeviceId_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Die Geräte-ID wird zurückgesetzt. Beim nächsten Start wird eine neue ID generiert.\n\nFortfahren?",
                "Geräte-ID zurücksetzen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                DeviceIdentityService.ResetDeviceId();
                MessageBox.Show(
                    "Die Geräte-ID wurde zurückgesetzt. Die neue ID wird erst beim nächsten Start aktiv.",
                    "Geräte-ID zurückgesetzt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DevSettingsWindow.BtnResetDeviceId_Click failed: {ex.Message}");
            }
        }

        private void BuildEditorRows()
        {
            pnlSettings.Children.Clear();
            _editors.Clear();

            foreach (var def in DevSettingsRegistry.All)
            {
                var current = GetCurrentValue(def.Key);

                var row = new Border
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = (System.Windows.Media.Brush)FindResource("CardBackgroundFillColorDefaultBrush")
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

                var keyBlock = new TextBlock
                {
                    Text = def.Key,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("AccentFillColorDefaultBrush")
                };
                Grid.SetColumn(keyBlock, 0);
                grid.Children.Add(keyBlock);

                var descBlock = new TextBlock
                {
                    Text = def.Description,
                    FontSize = 11,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 12, 0)
                };
                Grid.SetColumn(descBlock, 1);
                grid.Children.Add(descBlock);

                var editor = new TextBox
                {
                    Text = current,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 3, 6, 3)
                };
                editor.ToolTip = $"Default: {def.DefaultValue}";
                editor.LostFocus += Editor_LostFocus;
                Grid.SetColumn(editor, 2);
                grid.Children.Add(editor);

                _editors[def.Key] = editor;
                row.Child = grid;
                pnlSettings.Children.Add(row);
            }
        }

        private string GetCurrentValue(string key)
        {
            if (_settings.DevSettings != null && _settings.DevSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return DevSettingsRegistry.GetDefault(key);
        }

        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAll();
        }

        private void SaveAll()
        {
            try
            {
                if (_settings.DevSettings == null)
                    _settings.DevSettings = new Dictionary<string, string>();

                foreach (var kvp in _editors)
                {
                    var value = kvp.Value.Text?.Trim();
                    if (string.IsNullOrEmpty(value))
                    {
                        _settings.DevSettings.Remove(kvp.Key);
                    }
                    else
                    {
                        _settings.DevSettings[kvp.Key] = value;
                    }
                }

                _settingsService.Save(_settings);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DevSettingsWindow.SaveAll failed: {ex.Message}");
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.DevSettings = new Dictionary<string, string>();
                _settingsService.Save(_settings);
                BuildEditorRows();
            }
            catch (Exception ex)
            {
                LoggingService.Log($"DevSettingsWindow.BtnReset_Click failed: {ex.Message}");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SaveAll();
            Close();
        }
    }
}
