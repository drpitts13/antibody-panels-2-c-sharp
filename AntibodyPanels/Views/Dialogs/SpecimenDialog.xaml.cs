using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SpecimenDialog : Window
    {
        public string AccessionNumber => AccessionBox.Text.Trim();
        public string SpecimenType => TypeBox.SelectedItem?.ToString() ?? TypeBox.Text;
        public string? ExpirationDate => ExpirationPicker.SelectedDate?.ToString("yyyy-MM-dd");

        public SpecimenDialog(Specimen? existing = null)
        {
            InitializeComponent();

            TypeBox.ItemsSource = AntigenConstants.SpecimenTypes;

            if (existing == null)
            {
                // Adding a new specimen
                Title = "Add Specimen";
                TypeBox.SelectedIndex = 0;
            }
            else
            {
                // Editing an existing specimen — accession number is read-only
                Title = "Edit Specimen";
                AccessionBox.Text = existing.AccessionNumber;
                AccessionBox.IsEnabled = false;
                AccessionBox.Opacity = 0.6;

                var types = AntigenConstants.SpecimenTypes;
                int idx = -1;
                for (int i = 0; i < types.Count; i++) if (types[i] == existing.Type) { idx = i; break; }
                TypeBox.SelectedIndex = idx >= 0 ? idx : 0;

                if (existing.ExpirationDate != null &&
                    System.DateTime.TryParse(existing.ExpirationDate, out var d))
                    ExpirationPicker.SelectedDate = d;
            }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AccessionBox.Text))
            {
                MessageBox.Show("Accession number is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                AccessionBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}
