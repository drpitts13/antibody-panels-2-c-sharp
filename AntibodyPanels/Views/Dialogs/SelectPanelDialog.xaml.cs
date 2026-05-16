using System.Collections.Generic;
using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SelectPanelDialog : Window
    {
        public Panel? SelectedPanel => PanelList.SelectedItem as Panel;

        public SelectPanelDialog(IEnumerable<Panel> panels)
        {
            InitializeComponent();
            PanelList.ItemsSource = panels;
        }

        private void AttachClick(object sender, RoutedEventArgs e)
        {
            if (SelectedPanel == null)
            {
                MessageBox.Show("Please select a panel.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void PanelList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedPanel != null) DialogResult = true;
        }
    }
}
