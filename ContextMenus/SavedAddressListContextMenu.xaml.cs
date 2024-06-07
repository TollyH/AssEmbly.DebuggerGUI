using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for SavedAddressListContextMenu.xaml
    /// </summary>
    public partial class SavedAddressListContextMenu : ContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(SavedAddressListContextMenu sender);

        public event EventDelegate? AddressRemoved;
        public event EventDelegate? AddressAdded;

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
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            AddressRemoved?.Invoke(this);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            AddressAdded?.Invoke(this);
        }
    }
}
