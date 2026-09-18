using System.Linq;
using System.Windows;
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
            ApplyAntigenMarkStyle();
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

        private void ApplyAntigenMarkStyle()
        {
            var markStyle = new Style(typeof(TextBlock));
            markStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            markStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            markStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));

            foreach (var col in ResultsGrid.Columns.Skip(3).OfType<DataGridTextColumn>())
                col.ElementStyle = markStyle;
        }
    }
}
