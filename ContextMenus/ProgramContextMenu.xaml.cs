using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for ProgramContextMenu.xaml
    /// </summary>
    public partial class ProgramContextMenu : ContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(ProgramContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? BreakpointToggled;
        public event EventDelegate? Jumped;

        public ProgramContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public ProgramContextMenu(ulong address)
        {
            InitializeComponent();

            Address = address;
        }

        private void LabelItem_Click(object sender, RoutedEventArgs e)
        {
            LabelAdded?.Invoke(this);
        }

        private void AddressItem_Click(object sender, RoutedEventArgs e)
        {
            AddressSaved?.Invoke(this);
        }

        private void BreakpointItem_Click(object sender, RoutedEventArgs e)
        {
            BreakpointToggled?.Invoke(this);
        }

        private void JumpItem_Click(object sender, RoutedEventArgs e)
        {
            Jumped?.Invoke(this);
        }
    }
}
