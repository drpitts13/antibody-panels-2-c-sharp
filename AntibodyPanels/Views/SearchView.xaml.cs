using System.Windows.Controls;
using System.Windows.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Views
{
    public partial class SearchView : UserControl
    {
        public SearchView()
        {
            InitializeComponent();
            AddWarehouseColumns();
        }

        private void AddWarehouseColumns()
        {
            foreach (var ag in AntigenConstants.WarehouseAntigens)
            {
                ResultsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = ag,
                    Binding = new Binding($"Extra[{ag}]"),
                    Width = ag.Length >= 3 ? 44 : 40
                });
            }
        }
    }
}
