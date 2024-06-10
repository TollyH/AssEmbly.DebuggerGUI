using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for LabelListContextMenu.xaml
    /// </summary>
    public partial class LabelListContextMenu : ContextMenu, IAddressContextMenu
    {
        public string LabelName { get; }
        public ulong Address { get; }

        public delegate void EventDelegate(LabelListContextMenu sender);

        public event EventDelegate? LabelRemoved;
        public event EventDelegate? LabelAdded;
        public event EventDelegate? LabelDisassembling;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? MemoryScrolled;

        public LabelListContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }

            LabelName = "";
        }

        public LabelListContextMenu(string labelName, ulong address)
        {
            InitializeComponent();

            LabelName = labelName;
            Address = address;
            // Only show remove/assert item if this context menu has a label associated with it
            removeItem.Visibility = Visibility.Visible;
            disassembleItem.Visibility = Visibility.Visible;
            programItem.Visibility = Visibility.Visible;
            memoryItem.Visibility = Visibility.Visible;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            LabelRemoved?.Invoke(this);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            LabelAdded?.Invoke(this);
        }

        private void DisassembleItem_Click(object sender, RoutedEventArgs e)
        {
            LabelDisassembling?.Invoke(this);
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
