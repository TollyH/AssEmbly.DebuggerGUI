using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for SavedAddressListContextMenu.xaml
    /// </summary>
    public partial class SavedAddressListContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? AddressRemoved;
        public event EventDelegate? AddressAdded;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? MemoryScrolled;

        public SavedAddressListContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public SavedAddressListContextMenu(ulong address)
        {
            InitializeComponent();

            Address = address;
            // Only show remove item if this context menu has a saved address associated with it
            removeItem.Visibility = Visibility.Visible;
            programItem.Visibility = Visibility.Visible;
            memoryItem.Visibility = Visibility.Visible;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            AddressRemoved?.Invoke(this);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            AddressAdded?.Invoke(this);
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
