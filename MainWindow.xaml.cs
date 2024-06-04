using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Processor? DebuggingProcessor { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
        }

        public void LoadExecutable(string path)
        {
            AAPFile executable;
            try
            {
                executable = new(File.ReadAllBytes(path));
            }
            catch (Exception exc)
            {
                DialogPopup popup = new(exc.Message, "Error loading executable", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            if (!ulong.TryParse(memorySizeInput.Text, out ulong memorySize))
            {
                DialogPopup popup = new("Entered memory size is not valid.", "Error creating processor", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            try
            {
                DebuggingProcessor = new Processor(memorySize, executable.EntryPoint,
                    forceV1.IsChecked ?? false, mapStack.IsChecked ?? true, autoEcho.IsChecked ?? false);
                DebuggingProcessor.LoadProgram(executable.Program);
            }
            catch (Exception exc)
            {
                DialogPopup popup = new(exc.Message, "Error creating processor", DialogPopup.ErrorIcon)
                {
                    Owner = this
                };
                popup.ShowDialog();
                return;
            }

            executablePathText.Text = path;
        }

        private void AboutItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow window = new()
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "Assembled AssEmbly Programs (*.aap)|*.aap",
                Title = "Open Executable"
            };
            if (dialog.ShowDialog(this) ?? false)
            {
                LoadExecutable(dialog.FileName);
            }
        }
    }
}
