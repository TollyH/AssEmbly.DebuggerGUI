using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for WatchContextMenu.xaml
    /// </summary>
    public partial class WatchContextMenu : ContextMenu
    {
        public IBreakpoint? Watch { get; }

        public delegate void EventDelegate(WatchContextMenu sender);

        public event EventDelegate? WatchRemoved;

        public WatchContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public WatchContextMenu(IBreakpoint watch)
        {
            InitializeComponent();

            Watch = watch;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            WatchRemoved?.Invoke(this);
        }
    }
}
