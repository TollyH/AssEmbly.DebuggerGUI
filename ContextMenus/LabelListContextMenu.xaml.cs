using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for LabelListContextMenu.xaml
    /// </summary>
    public partial class LabelListContextMenu : ContextMenu
    {
        public string LabelName { get; }

        public delegate void EventDelegate(LabelListContextMenu sender);

        public event EventDelegate? LabelRemoved;
        public event EventDelegate? LabelAdded;
        public event EventDelegate? LabelDisassembling;

        public LabelListContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }

            LabelName = "";
        }

        public LabelListContextMenu(string labelName)
        {
            InitializeComponent();

            LabelName = labelName;
            // Only show remove/assert item if this context menu has a label associated with it
            removeItem.Visibility = Visibility.Visible;
            disassembleItem.Visibility = Visibility.Visible;
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
    }
}
