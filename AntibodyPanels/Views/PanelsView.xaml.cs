using System.Windows.Controls;

namespace AntibodyPanels.Views
{
    public partial class PanelsView : UserControl
    {
        public static readonly string[] AntigenValues = { "+", "-" };

        public PanelsView()
        {
            InitializeComponent();
        }
    }
}
