using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    public partial class WorklistView : UserControl
    {
        public WorklistView()
        {
            InitializeComponent();
        }

        private void WorklistGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is WorklistViewModel vm && vm.OpenCommand.CanExecute(null))
                vm.OpenCommand.Execute(null);
        }
    }
}
