using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for BreakpointListContextMenu.xaml
    /// </summary>
    public partial class BreakpointListContextMenu : ContextMenu, IAddressContextMenu
    {
        public RegisterValueBreakpoint Breakpoint { get; }

        public ulong Address
        {
            get
            {
                if (Breakpoint.CheckRegister != Register.rpo)
                {
                    throw new InvalidOperationException(
                        "This breakpoint has no associated address as it is not targeted to the rpo register.");
                }
                return Breakpoint.TargetValue;
            }
        }

        public delegate void EventDelegate(BreakpointListContextMenu sender);

        public event EventDelegate? BreakpointRemoved;
        public event EventDelegate? BreakpointAdded;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? MemoryScrolled;

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
            programItem.Visibility = Visibility.Visible;
            memoryItem.Visibility = Visibility.Visible;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            BreakpointRemoved?.Invoke(this);
        }

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            BreakpointAdded?.Invoke(this);
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
