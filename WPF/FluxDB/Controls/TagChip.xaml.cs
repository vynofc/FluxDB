using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluxDB.Views.Controls
{
    public partial class TagChip : UserControl
    {
        public static readonly DependencyProperty TagNameProperty =
            DependencyProperty.Register("TagName", typeof(string), typeof(TagChip), new PropertyMetadata(""));

        public static readonly DependencyProperty ChipBackgroundProperty =
            DependencyProperty.Register("ChipBackground", typeof(Brush), typeof(TagChip),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"))));

        public string TagName
        {
            get => (string)GetValue(TagNameProperty);
            set => SetValue(TagNameProperty, value);
        }

        public Brush ChipBackground
        {
            get => (Brush)GetValue(ChipBackgroundProperty);
            set => SetValue(ChipBackgroundProperty, value);
        }

        public event RoutedEventHandler RemoveClicked;

        public TagChip()
        {
            InitializeComponent();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            RemoveClicked?.Invoke(this, e);
        }
    }
}