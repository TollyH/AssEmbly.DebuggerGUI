using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for MemoryPersistentEditContextMenu.xaml
    /// </summary>
    public partial class MemoryPersistentEditContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? EditRemoved;

        public MemoryPersistentEditContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public MemoryPersistentEditContextMenu(ulong address)
        {
            InitializeComponent();

            Address = address;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            EditRemoved?.Invoke(this);
        }
    }
}
