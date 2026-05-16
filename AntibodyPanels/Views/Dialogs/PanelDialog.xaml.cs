using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class PanelDialog : Window
    {
        public string PanelName => NameBox.Text.Trim();
        public string? LotNumber => string.IsNullOrWhiteSpace(LotBox.Text) ? null : LotBox.Text.Trim();
        public string? Vendor => string.IsNullOrWhiteSpace(VendorBox.Text) ? null : VendorBox.Text.Trim();
        public int NumCells => int.TryParse(NumCellsBox.Text, out var n) ? n : 16;
        public int StartCell => int.TryParse(StartCellBox.Text, out var s) ? s : 1;
        public string? ExpirationDate => ExpirationPicker.SelectedDate?.ToString("yyyy-MM-dd");
        public bool IncludeAc => IncludeAcCheck.IsChecked == true;

        public PanelDialog(Panel? existing = null)
        {
            InitializeComponent();
            if (existing != null)
            {
                Title = "Edit Panel";
                NameBox.Text = existing.Name;
                LotBox.Text = existing.LotNumber;
                VendorBox.Text = existing.Vendor;
                NumCellsBox.Text = existing.NumCells.ToString();
                StartCellBox.Text = existing.StartCell.ToString();
                IncludeAcCheck.IsChecked = existing.IncludeAc;
                if (existing.ExpirationDate != null &&
                    System.DateTime.TryParse(existing.ExpirationDate, out var d))
                    ExpirationPicker.SelectedDate = d;
            }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Panel name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }
            if (!int.TryParse(NumCellsBox.Text, out var n) || n < 1 || n > 100)
            {
                MessageBox.Show("Number of cells must be between 1 and 100.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NumCellsBox.Focus();
                return;
            }
            if (!int.TryParse(StartCellBox.Text, out var s) || s < 1)
            {
                MessageBox.Show("Starting cell number must be 1 or greater.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StartCellBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}
