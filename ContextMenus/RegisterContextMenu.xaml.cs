using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for RegisterContextMenu.xaml
    /// </summary>
    public partial class RegisterContextMenu : ContextMenu
    {
        public static readonly DependencyProperty RepresentedRegisterProperty = DependencyProperty.Register(
            nameof(RepresentedRegister), typeof(Register), typeof(RegisterContextMenu));

        public Register RepresentedRegister
        {
            get => (Register)GetValue(RepresentedRegisterProperty);
            set => SetValue(RepresentedRegisterProperty, value);
        }

        public delegate void EventDelegate(RegisterContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? ProgramScrolled;
        public event EventDelegate? MemoryScrolled;
        public event EventDelegate? ValueWatchAdded;
        public event EventDelegate? ChangeWatchAdded;

        public RegisterContextMenu()
        {
            if (!IsInitialized)
            {
                InitializeComponent();
            }
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

        private void MemoryItem_Click(object sender, RoutedEventArgs e)
        {
            MemoryScrolled?.Invoke(this);
        }

        private void WatchValueItem_Click(object sender, RoutedEventArgs e)
        {
            ValueWatchAdded?.Invoke(this);
        }

        private void WatchChangeItem_Click(object sender, RoutedEventArgs e)
        {
            ChangeWatchAdded?.Invoke(this);
        }
    }
}
