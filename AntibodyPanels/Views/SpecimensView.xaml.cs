using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    public partial class SpecimensView : UserControl
    {
        public SpecimensView()
        {
            InitializeComponent();
        }

        private void SpecimensGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is SpecimensViewModel vm && vm.EditCommand.CanExecute(null))
                vm.EditCommand.Execute(null);
        }
    }
}
