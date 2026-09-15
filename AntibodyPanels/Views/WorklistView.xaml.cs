using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.Models;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    public partial class WorklistView : UserControl
    {
        public WorklistView()
        {
            InitializeComponent();
        }

        private void WorklistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGridRow { Item: WorklistItem item }) return;
            if (DataContext is not WorklistViewModel vm) return;
            vm.OpenItem(item);
            e.Handled = true;
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
