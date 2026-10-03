using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class PanelCompareDialog : Window
    {
        private readonly DatabaseService _db;
        private readonly Models.Panel _selected;

        public PanelCompareDialog(DatabaseService db, Models.Panel selected, IReadOnlyList<Models.Panel> others, int? preferredId)
        {
            InitializeComponent();
            _db = db;
            _selected = selected;
            OtherPanelBox.ItemsSource = others;
            OtherPanelBox.SelectedItem = others.FirstOrDefault(p => p.PanelId == preferredId)
                ?? others.FirstOrDefault();
            RefreshCompare();
        }

        private void OtherPanelChanged(object sender, SelectionChangedEventArgs e) => RefreshCompare();

        private void RefreshCompare()
        {
            if (OtherPanelBox.SelectedItem is not Models.Panel other)
            {
                ExplanationBox.Text = "Select another panel to compare.";
                DiffGrid.ItemsSource = null;
                return;
            }

            var result = PanelComparer.Compare(
                PanelComparer.PanelLabel(_selected),
                _db.GetPanelCells(_selected.PanelId),
                _db.GetPanelDisplayAntigens(_selected.PanelId),
                PanelComparer.PanelLabel(other),
                _db.GetPanelCells(other.PanelId),
                _db.GetPanelDisplayAntigens(other.PanelId));
            ExplanationBox.Text = result.Explanation;
            DiffGrid.ItemsSource = result.TypingDiffs;
        }
    }
}
