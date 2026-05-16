using System.Linq;
using System.Windows;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class RuleDialog : Window
    {
        public string RuleName => NameBox.Text.Trim();
        public string? Description => string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim();
        public string Antibody => AntibodyBox.Text.Trim();
        public string? ExceptionAntigen => string.IsNullOrWhiteSpace(ExceptionAntigenBox.Text) ? null : ExceptionAntigenBox.Text.Trim();
        public bool HeterozygousOk => HeterozygousCheck.IsChecked == true;
        public int MinRuleoutCount => int.TryParse(MinCountBox.Text, out var n) ? n : 3;

        public RuleDialog(Rule? existing = null)
        {
            InitializeComponent();
            var antibodies = AntigenConstants.Antigens.Select(ag => $"anti-{ag}").ToList();
            AntibodyBox.ItemsSource = antibodies;
            ExceptionAntigenBox.ItemsSource = AntigenConstants.Antigens.ToList();

            if (existing != null)
            {
                NameBox.Text = existing.Name;
                AntibodyBox.Text = existing.Antibody;
                DescBox.Text = existing.Description;
                ExceptionAntigenBox.Text = existing.ExceptionAntigen;
                HeterozygousCheck.IsChecked = existing.HeterozygousOk;
                MinCountBox.Text = existing.MinRuleoutCount.ToString();
            }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(AntibodyBox.Text))
            {
                MessageBox.Show("Name and Antibody are required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
