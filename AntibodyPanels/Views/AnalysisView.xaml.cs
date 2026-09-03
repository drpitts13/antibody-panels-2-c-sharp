using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    public partial class AnalysisView : UserControl
    {
        public AnalysisView()
        {
            InitializeComponent();
        }

        private void SuspectedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is AnalysisViewModel vm && vm.AddSelectedToFinalIdCommand.CanExecute(null))
                vm.AddSelectedToFinalIdCommand.Execute(null);
        }
    }
}
