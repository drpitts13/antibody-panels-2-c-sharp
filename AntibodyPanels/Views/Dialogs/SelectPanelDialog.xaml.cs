using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class SelectPanelDialog : Window
    {
        private readonly List<Models.Panel> _all;

        public Models.Panel? SelectedPanel => PanelList.SelectedItem as Models.Panel;

        public SelectPanelDialog(IEnumerable<Models.Panel> panels)
        {
            InitializeComponent();
            _all = panels.ToList();
            ApplyFilter();
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        internal void ApplyFilter(string? query = null)
        {
            query ??= FilterBox.Text;
            var keep = SelectedPanel?.PanelId;
            var filtered = _all.Where(p => p.MatchesFilter(query)).ToList();
            PanelList.ItemsSource = filtered;
            PanelList.SelectedItem = filtered.FirstOrDefault(p => p.PanelId == keep)
                ?? filtered.FirstOrDefault();
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
