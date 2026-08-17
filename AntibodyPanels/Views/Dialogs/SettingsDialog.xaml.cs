using System.Globalization;
using System.Windows;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            var s = AppSettings.Current;
            LabNameBox.Text = s.LabName;
            DepartmentBox.Text = s.Department;
            ThresholdBox.Text = s.ProbabilityThreshold.ToString("0.00", CultureInfo.InvariantCulture);
            DefaultTypeBox.ItemsSource = AntigenConstants.SpecimenTypes;
            DefaultTypeBox.SelectedItem = s.DefaultSpecimenType;
            if (DefaultTypeBox.SelectedItem == null) DefaultTypeBox.SelectedIndex = 0;
            ExpiryDaysBox.Text = s.ExpirationWarningDays.ToString();
            ShowInactiveCheck.IsChecked = s.ShowInactiveByDefault;
            HideRuledOutCheck.IsChecked = s.HideRuledOutAntigenColumns;
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ThresholdBox.Text.Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var threshold) ||
                threshold < 0.3 || threshold > 0.95)
            {
                MessageBox.Show("Score threshold must be a number between 0.30 and 0.95.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                ThresholdBox.Focus();
                return;
            }
            if (!int.TryParse(ExpiryDaysBox.Text.Trim(), out var days) || days < 1 || days > 90)
            {
                MessageBox.Show("Expiration warning days must be between 1 and 90.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                ExpiryDaysBox.Focus();
                return;
            }

            AppSettings.Current.LabName = LabNameBox.Text.Trim();
            AppSettings.Current.Department = DepartmentBox.Text.Trim();
            AppSettings.Current.ProbabilityThreshold = threshold;
            AppSettings.Current.DefaultSpecimenType = DefaultTypeBox.SelectedItem?.ToString() ?? "serum";
            AppSettings.Current.ExpirationWarningDays = days;
            AppSettings.Current.ShowInactiveByDefault = ShowInactiveCheck.IsChecked == true;
            AppSettings.Current.HideRuledOutAntigenColumns = HideRuledOutCheck.IsChecked == true;
            SettingsService.Save();
            DialogResult = true;
        }
    }
}
