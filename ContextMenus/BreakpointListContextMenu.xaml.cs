using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for BreakpointListContextMenu.xaml
    /// </summary>
    public partial class BreakpointListContextMenu : ContextMenu
    {
        public RegisterValueBreakpoint Breakpoint { get; }

        public delegate void EventDelegate(BreakpointListContextMenu sender);

        public event EventDelegate? BreakpointRemoved;
        public event EventDelegate? BreakpointAdded;

        public BreakpointListContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public BreakpointListContextMenu(RegisterValueBreakpoint breakpoint)
        {
            InitializeComponent();

            Breakpoint = breakpoint;
            // Only show remove item if this context menu has a breakpoint associated with it
            removeItem.Visibility = Visibility.Visible;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            BreakpointRemoved?.Invoke(this);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            BreakpointAdded?.Invoke(this);
        }
    }
}
