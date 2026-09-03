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
            OpenSelected();
        }

        private void WorklistGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            OpenSelected();
            e.Handled = true;
        }

        private void OpenSelected()
        {
            if (DataContext is WorklistViewModel vm && vm.OpenCommand.CanExecute(null))
                vm.OpenCommand.Execute(null);
        }
    }
}
