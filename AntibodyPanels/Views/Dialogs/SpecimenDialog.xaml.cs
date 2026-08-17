using System;
using System.Windows;
using System.Windows.Controls;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SpecimenDialog : Window
    {
        public string AccessionNumber => AccessionBox.Text.Trim();
        public string SpecimenType => TypeBox.SelectedItem?.ToString() ?? TypeBox.Text;
        public string? ExpirationDate => ExpirationPicker.SelectedDate?.ToString("yyyy-MM-dd");
        public bool ItemIsActive => ActiveCheck.IsChecked == true;
        public string? Notes => string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        public string? Phenotype => string.IsNullOrWhiteSpace(PhenotypeBox.Text) ? null : PhenotypeBox.Text.Trim();
        public string? PreviousAntibodies => string.IsNullOrWhiteSpace(PreviousAbsBox.Text) ? null : PreviousAbsBox.Text.Trim();
        public string? DatResult => DatBox.SelectedItem?.ToString();

        public SpecimenDialog(Specimen? existing = null)
        {
            InitializeComponent();

            TypeBox.ItemsSource = AntigenConstants.SpecimenTypes;
            DatBox.ItemsSource = AntigenConstants.DatResults;

            if (existing == null)
            {
                Title = "Add Specimen";
                var def = AppSettings.Current.DefaultSpecimenType;
                int t = -1;
                for (int i = 0; i < AntigenConstants.SpecimenTypes.Count; i++)
                    if (AntigenConstants.SpecimenTypes[i] == def) { t = i; break; }
                TypeBox.SelectedIndex = t >= 0 ? t : 0;
                DatBox.SelectedIndex = 0;
                ActiveCheck.IsChecked = true;
            }
            else
            {
                Title = "Edit Specimen";
                AccessionBox.Text = existing.AccessionNumber;
                AccessionBox.IsEnabled = false;
                AccessionBox.Opacity = 0.6;

                var types = AntigenConstants.SpecimenTypes;
                int idx = -1;
                for (int i = 0; i < types.Count; i++) if (types[i] == existing.Type) { idx = i; break; }
                TypeBox.SelectedIndex = idx >= 0 ? idx : 0;

                if (existing.ExpirationDate != null &&
                    DateTime.TryParse(existing.ExpirationDate, out var d))
                    ExpirationPicker.SelectedDate = d;

                ActiveCheck.IsChecked = existing.IsActive;
                PhenotypeBox.Text = existing.Phenotype ?? "";
                PreviousAbsBox.Text = existing.PreviousAntibodies ?? "";
                NotesBox.Text = existing.Notes ?? "";

                int datIdx = 0;
                for (int i = 0; i < AntigenConstants.DatResults.Count; i++)
                    if (AntigenConstants.DatResults[i] == existing.DatResult) { datIdx = i; break; }
                DatBox.SelectedIndex = datIdx;
            }
        }

        private void ExpirationPicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExpirationPicker.SelectedDate.HasValue &&
                ExpirationPicker.SelectedDate.Value.Date < DateTime.Today)
            {
                ActiveCheck.IsChecked = false;
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
