using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SelectWarehouseAntigenDialog : Window
    {
        public IReadOnlyList<string> SelectedAntigens =>
            AntigenList.SelectedItems.Cast<WarehouseAntigenDefinition>().Select(d => d.Name).ToList();

        public SelectWarehouseAntigenDialog(
            IEnumerable<WarehouseAntigenDefinition> antigens,
            string title,
            string prompt,
            string confirmText = "Add")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            OkButton.Content = confirmText;
            AntigenList.ItemsSource = antigens;
            if (AntigenList.Items.Count > 0)
                AntigenList.SelectedIndex = 0;
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            if (SelectedAntigens.Count == 0)
            {
                MessageBox.Show("Please select at least one antigen.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void AntigenList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedAntigens.Count > 0) DialogResult = true;
        }
    }
}
