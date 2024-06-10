﻿using System.Windows;
using System.Windows.Controls;

namespace AssEmbly.DebuggerGUI.ContextMenus
{
    /// <summary>
    /// Interaction logic for MemoryContextMenu.xaml
    /// </summary>
    public partial class MemoryContextMenu : ContextMenu, IAddressContextMenu
    {
        public ulong Address { get; }

        public delegate void EventDelegate(IAddressContextMenu sender);

        public event EventDelegate? LabelAdded;
        public event EventDelegate? AddressSaved;
        public event EventDelegate? ProgramScrolled;

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
    }
}
