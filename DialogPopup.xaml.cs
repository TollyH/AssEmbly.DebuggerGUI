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
        public const string QuestionIcon = "pack://application:,,,/Icons/question-32.png";

        public string InputText
        {
            get => inputBox.Text;
            set => inputBox.Text = value;
        }

        public DialogPopup(string message, string title, string image, bool showInputBox = false)
        {
            InitializeComponent();

            Title = title;
            messageText.Text = message;
            dialogImage.Source = new BitmapImage(new Uri(image));

            inputBox.Visibility = showInputBox ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
