using System.Windows;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void AboutItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow window = new()
            {
                Owner = this
            };
            window.ShowDialog();
        }
    }
}
