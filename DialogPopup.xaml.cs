using System.Windows;
using System.Windows.Media.Imaging;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for DialogPopup.xaml
    /// </summary>
    public partial class DialogPopup : Window
    {
        public const string ErrorIcon = "pack://application:,,,/Icons/cross-circle-32.png";

        public DialogPopup(string message, string title, string image)
        {
            InitializeComponent();

            Title = title;
            messageText.Text = message;
            dialogImage.Source = new BitmapImage(new Uri(image));
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
