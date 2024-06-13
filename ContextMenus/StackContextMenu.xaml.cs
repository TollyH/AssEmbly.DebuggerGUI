using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for StackContextMenu.xaml
    /// </summary>
    public partial class StackContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? MemoryScrolled;

        public StackContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public StackContextMenu(ulong address)
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

        private void ProgramItem_Click(object sender, RoutedEventArgs e)
        {
            ProgramScrolled?.Invoke(this);
        }

        private void MemoryItem_Click(object sender, RoutedEventArgs e)
        {
            MemoryScrolled?.Invoke(this);
        }
    }
}
