using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for ProgramContextMenu.xaml
    /// </summary>
    public partial class ProgramContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; set; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? BreakpointToggled;
        public event EventDelegate? Jumped;
        public event EventDelegate? Edited;
        public event EventDelegate? MemoryScrolled;
        public event EventDelegate? AddressCopied;

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

        private void EditItem_Click(object sender, RoutedEventArgs e)
        {
            Edited?.Invoke(this);
        }

        private void MemoryItem_Click(object sender, RoutedEventArgs e)
        {
            MemoryScrolled?.Invoke(this);
        }

        private void CopyAddressItem_Click(object sender, RoutedEventArgs e)
        {
            AddressCopied?.Invoke(this);
        }
    }
}
