using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for MemoryContextMenu.xaml
    /// </summary>
    public partial class MemoryContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; set; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? Value8WatchAdded;
        public event EventDelegate? Value16WatchAdded;
        public event EventDelegate? Value32WatchAdded;
        public event EventDelegate? Value64WatchAdded;
        public event EventDelegate? Change8WatchAdded;
        public event EventDelegate? Change16WatchAdded;
        public event EventDelegate? Change32WatchAdded;
        public event EventDelegate? Change64WatchAdded;
        public event EventDelegate? Edited8;
        public event EventDelegate? PersistentEdited8;
        public event EventDelegate? Edited16;
        public event EventDelegate? PersistentEdited16;
        public event EventDelegate? Edited32;
        public event EventDelegate? PersistentEdited32;
        public event EventDelegate? Edited64;
        public event EventDelegate? PersistentEdited64;

        public MemoryContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public MemoryContextMenu(ulong address)
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

        private void WatchValue8Item_Click(object sender, RoutedEventArgs e)
        {
            Value8WatchAdded?.Invoke(this);
        }

        private void WatchValue16Item_Click(object sender, RoutedEventArgs e)
        {
            Value16WatchAdded?.Invoke(this);
        }

        private void WatchValue32Item_Click(object sender, RoutedEventArgs e)
        {
            Value32WatchAdded?.Invoke(this);
        }

        private void WatchValue64Item_Click(object sender, RoutedEventArgs e)
        {
            Value64WatchAdded?.Invoke(this);
        }

        private void WatchChange8Item_Click(object sender, RoutedEventArgs e)
        {
            Change8WatchAdded?.Invoke(this);
        }

        private void WatchChange16Item_Click(object sender, RoutedEventArgs e)
        {
            Change16WatchAdded?.Invoke(this);
        }

        private void WatchChange32Item_Click(object sender, RoutedEventArgs e)
        {
            Change32WatchAdded?.Invoke(this);
        }

        private void WatchChange64Item_Click(object sender, RoutedEventArgs e)
        {
            Change64WatchAdded?.Invoke(this);
        }

        private void Edit8Item_Click(object sender, RoutedEventArgs e)
        {
            Edited8?.Invoke(this);
        }

        private void EditPersistent8Item_Click(object sender, RoutedEventArgs e)
        {
            PersistentEdited8?.Invoke(this);
        }

        private void Edit16Item_Click(object sender, RoutedEventArgs e)
        {
            Edited16?.Invoke(this);
        }

        private void EditPersistent16Item_Click(object sender, RoutedEventArgs e)
        {
            PersistentEdited16?.Invoke(this);
        }

        private void Edit32Item_Click(object sender, RoutedEventArgs e)
        {
            Edited32?.Invoke(this);
        }

        private void EditPersistent32Item_Click(object sender, RoutedEventArgs e)
        {
            PersistentEdited32?.Invoke(this);
        }

        private void Edit64Item_Click(object sender, RoutedEventArgs e)
        {
            Edited64?.Invoke(this);
        }

        private void EditPersistent64Item_Click(object sender, RoutedEventArgs e)
        {
            PersistentEdited64?.Invoke(this);
        }
    }
}
