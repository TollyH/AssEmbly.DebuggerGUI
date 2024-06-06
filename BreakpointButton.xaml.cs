using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI
{
    /// <summary>
    /// Interaction logic for BreakpointButton.xaml
    /// </summary>
    public partial class BreakpointButton : CheckBox
    {
        public ulong Address { get; set; }

        public BreakpointButton()
        {
            InitializeComponent();
        }
    }
}
