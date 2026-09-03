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
            IdRuleBox.ItemsSource = new[] { "1 + 1", "2 + 2", "3 + 3" };
            var idCount = s.IdentificationCellCount;
            if (idCount < 1 || idCount > 3) idCount = 3;
            IdRuleBox.SelectedItem = $"{idCount} + {idCount}";
            AcsRuleoutBox.Text = s.AcsRuleoutCount.ToString();
            DefaultTypeBox.ItemsSource = AntigenConstants.SpecimenTypes;
            DefaultTypeBox.SelectedItem = s.DefaultSpecimenType;
            if (DefaultTypeBox.SelectedItem == null) DefaultTypeBox.SelectedIndex = 0;
            DatingDaysBox.Text = s.DefaultSpecimenDatingDays.ToString();
            ExpiryDaysBox.Text = s.ExpirationWarningDays.ToString();
            MaxDbSizeBox.Text = s.MaxDatabaseSizeMb.ToString();
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
            if (!int.TryParse(AcsRuleoutBox.Text.Trim(), out var acsCount) || acsCount < 1 || acsCount > 5)
            {
                MessageBox.Show("ACS rule-outs must be a number between 1 and 5.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                AcsRuleoutBox.Focus();
                return;
            }
            if (!int.TryParse(DatingDaysBox.Text.Trim(), out var datingDays) || datingDays < 0 || datingDays > 14)
            {
                MessageBox.Show("Specimen dating days must be between 0 and 14. Use 0 to leave expiration blank.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                DatingDaysBox.Focus();
                return;
            }
            if (!int.TryParse(ExpiryDaysBox.Text.Trim(), out var days) || days < 1 || days > 90)
            {
                MessageBox.Show("Expiration warning days must be between 1 and 90.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                ExpiryDaysBox.Focus();
                return;
            }
            if (!int.TryParse(MaxDbSizeBox.Text.Trim(), out var maxMb) || maxMb < 50 || maxMb > 10240)
            {
                MessageBox.Show("Maximum database size must be between 50 and 10240 MB.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                MaxDbSizeBox.Focus();
                return;
            }

            AppSettings.Current.LabName = LabNameBox.Text.Trim();
            AppSettings.Current.Department = DepartmentBox.Text.Trim();
            AppSettings.Current.ProbabilityThreshold = threshold;
            var idRule = IdRuleBox.SelectedItem?.ToString() ?? "3 + 3";
            AppSettings.Current.IdentificationCellCount =
                idRule.StartsWith("1") ? 1 : idRule.StartsWith("2") ? 2 : 3;
            AppSettings.Current.AcsRuleoutCount = acsCount;
            AppSettings.Current.DefaultSpecimenType = DefaultTypeBox.SelectedItem?.ToString() ?? "serum";
            AppSettings.Current.DefaultSpecimenDatingDays = datingDays;
            AppSettings.Current.ExpirationWarningDays = days;
            AppSettings.Current.MaxDatabaseSizeMb = maxMb;
            AppSettings.Current.ShowInactiveByDefault = ShowInactiveCheck.IsChecked == true;
            AppSettings.Current.HideRuledOutAntigenColumns = HideRuledOutCheck.IsChecked == true;
            SettingsService.Save();
            DialogResult = true;
        }
    }
}
