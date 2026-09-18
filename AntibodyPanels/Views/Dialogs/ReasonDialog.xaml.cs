using System.Windows;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class ReasonDialog : Window
    {
        public string Reason { get; private set; } = "";

        public ReasonDialog(string prompt)
        {
            InitializeComponent();
            PromptText.Text = prompt;
            ReasonBox.Focus();
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            var reason = ReasonBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("A reason is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ReasonBox.Focus();
                return;
            }

            Reason = reason;
            DialogResult = true;
        }
    }
}
