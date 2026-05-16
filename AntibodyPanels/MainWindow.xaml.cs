using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            // Wire commands onto the VM
            vm.ExitCommand = new RelayCommand(Close);
            vm.SaveCurrentCommand = new RelayCommand(SaveCurrent);
            vm.RefreshAllCommand = new RelayCommand(vm.RefreshAll);
            vm.NewItemCommand = new RelayCommand(NewItem);
            vm.ShowShortcutsCommand = new RelayCommand(ShowShortcuts);
            vm.ShowAboutCommand = new RelayCommand(ShowAbout);

            // F1 shortcut
            InputBindings.Add(new KeyBinding(vm.ShowShortcutsCommand, Key.F1, ModifierKeys.None));

            Closing += (s, e) =>
            {
                if (MessageBox.Show("Quit the application?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    e.Cancel = true;
                else
                    vm.Dispose();
            };
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl.SelectedIndex == 3) // Analysis tab
                ViewModel.AnalysisVM.OnTabSelected();
        }

        private void SaveCurrent()
        {
            switch (MainTabControl.SelectedIndex)
            {
                case 1: ViewModel.PanelsVM.SaveAllCells(); break;
                case 2: ViewModel.ReactionsVM.SaveAnalyzeCommand.Execute(null); break;
                default: ViewModel.SetStatus("No save action for this tab."); break;
            }
        }

        private void NewItem()
        {
            switch (MainTabControl.SelectedIndex)
            {
                case 0: ViewModel.SpecimensVM.AddCommand.Execute(null); break;
                case 1: ViewModel.PanelsVM.AddCommand.Execute(null); break;
                case 6: ViewModel.RulesVM.AddCommand.Execute(null); break;
                default: ViewModel.SetStatus("No new item action for this tab."); break;
            }
        }

        private void ShowShortcuts()
        {
            MessageBox.Show(
                "Keyboard Shortcuts\n\n" +
                "Ctrl+S     Save current tab\n" +
                "Ctrl+R     Refresh all tabs\n" +
                "Ctrl+N     New item (context-aware)\n" +
                "F1         Show this help\n\n" +
                "Reaction Entry:\n" +
                "  Select cell in grid and choose value from dropdown\n\n" +
                "Panel Antigen Grid:\n" +
                "  Select cell and choose + or - from dropdown\n" +
                "  Click 'Save All Changes' to persist",
                "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Antibody Panel Management System\n\n" +
                "Version 2.0 (C# / WPF)\n\n" +
                "A comprehensive system for managing antibody panels,\n" +
                "specimen reactions, and antibody identification analysis.\n\n" +
                "Press F1 for keyboard shortcuts.",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
