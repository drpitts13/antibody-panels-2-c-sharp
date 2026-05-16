using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AntibodyPanels.Views
{
    public partial class SearchView : UserControl
    {
        public SearchView()
        {
            InitializeComponent();
        }

        // Walks the DataGrid's visual tree to find its internal ScrollViewer.
        private static ScrollViewer? GetDataGridScrollViewer(DataGrid dg)
        {
            if (VisualTreeHelper.GetChildrenCount(dg) == 0) return null;
            if (VisualTreeHelper.GetChild(dg, 0) is not Decorator border) return null;
            return border.Child as ScrollViewer;
        }

        private void CriteriaScrollUp_Click(object sender, RoutedEventArgs e)
            => GetDataGridScrollViewer(CriteriaGrid)?.LineUp();

        private void CriteriaScrollDown_Click(object sender, RoutedEventArgs e)
            => GetDataGridScrollViewer(CriteriaGrid)?.LineDown();
    }
}
