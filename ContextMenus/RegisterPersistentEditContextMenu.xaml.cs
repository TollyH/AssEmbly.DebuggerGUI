using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for RegisterPersistentEditContextMenu.xaml
    /// </summary>
    public partial class RegisterPersistentEditContextMenu : ContextMenu
    {
        public Register RepresentedRegister { get; }

        public delegate void EventDelegate(RegisterPersistentEditContextMenu sender);

        public event EventDelegate? EditRemoved;

        public RegisterPersistentEditContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
        }

        public RegisterPersistentEditContextMenu(Register register)
        {
            InitializeComponent();

            RepresentedRegister = register;
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            EditRemoved?.Invoke(this);
        }
    }
}
