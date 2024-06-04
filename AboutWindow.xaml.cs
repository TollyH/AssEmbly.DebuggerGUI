using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            Version? version = typeof(AboutWindow).Assembly.GetName().Version;
            versionText.Text = $"GUI Version: {version?.Major}.{version?.Minor}.{version?.Build}";
            version = typeof(Processor).Assembly.GetName().Version;
            versionAssEmblyText.Text = $"Bundled AssEmbly Version: {version?.Major}.{version?.Minor}.{version?.Build}";

            object[] attributes = typeof(AboutWindow).Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
            copyrightText.Text = attributes.Length == 0 ? "" : ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
        }

        private void IconAttribution_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("http://p.yusukekamiyamane.com/")
            {
                UseShellExecute = true
            });
        }
    }
}
