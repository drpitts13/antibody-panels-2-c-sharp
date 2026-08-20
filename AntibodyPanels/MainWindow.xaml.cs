using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.Models;
using AntibodyPanels.Services;
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
            vm.LoadDemoDataCommand = new RelayCommand(LoadDemoData);
            vm.ShowSettingsCommand = new RelayCommand(ShowSettings);
            vm.ShowPurgeDatabaseCommand = new RelayCommand(ShowPurgeDatabase);
            vm.ShowOpenArchiveCommand = new RelayCommand(ShowOpenArchive);

            // F1 shortcut
            InputBindings.Add(new KeyBinding(vm.ShowShortcutsCommand, Key.F1, ModifierKeys.None));

            Loaded += MainWindow_Loaded;

            Closing += (s, e) =>
            {
                if (MessageBox.Show("Quit the application?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    e.Cancel = true;
                else
                    vm.Dispose();
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.NotifyCapacityIfNeeded();
            if (!ViewModel.IsDatabaseNearCapacity) return;

            var cap = ViewModel.GetCapacityStatus();
            var result = MessageBox.Show(
                $"The database is {cap.PercentUsed:0}% of the configured maximum size " +
                $"({DatabaseCapacityStatus.FormatBytes(cap.FileBytes)} / {DatabaseCapacityStatus.FormatBytes(cap.MaxBytes)}).\n\n" +
                "Would you like to purge old specimen data?",
                "Database nearly full",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                ShowPurgeDatabase();
        }

        private void ShowPurgeDatabase()
        {
            var dlg = new Views.Dialogs.PurgeDatabaseDialog(ViewModel.Database) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ViewModel.RefreshAll();
            var status = $"Purged {dlg.DeletedCount} specimen(s). Database is now {dlg.SizeAfterDisplay}.";
            if (!string.IsNullOrEmpty(dlg.ArchivePath))
                status += $" Archive: {dlg.ArchivePath}";
            ViewModel.SetStatus(status);
            ViewModel.NotifyCapacityIfNeeded();

            var archiveNote = string.IsNullOrEmpty(dlg.ArchivePath)
                ? ""
                : $"\n\nArchive saved to:\n{dlg.ArchivePath}";
            MessageBox.Show(
                $"Removed {dlg.DeletedCount} specimen(s).\nDatabase size is now {dlg.SizeAfterDisplay}.{archiveNote}",
                "Purge complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ShowOpenArchive()
        {
            var dlg = new Views.Dialogs.RestoreArchiveDialog(ViewModel.Database) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.RestoreResult == null) return;

            ViewModel.RefreshAll();
            var result = dlg.RestoreResult;
            var status =
                $"Restored {result.SpecimensRestored} specimen(s)" +
                (result.SpecimensSkipped > 0 ? $", skipped {result.SpecimensSkipped} already present" : "") +
                ".";
            ViewModel.SetStatus(status);
            ViewModel.NotifyCapacityIfNeeded();
            MessageBox.Show(
                status +
                (result.PanelsRestored > 0 ? $"\nAlso restored {result.PanelsRestored} panel(s)." : ""),
                "Restore complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl) return;
            if (MainTabControl.SelectedItem is TabItem { Header: "Analysis" })
                ViewModel.AnalysisVM.OnTabSelected();
            else if (MainTabControl.SelectedItem is TabItem { Header: "Worklist" })
                ViewModel.WorklistVM.Refresh();
        }

        private void SaveCurrent()
        {
            switch ((MainTabControl.SelectedItem as TabItem)?.Header as string)
            {
                case "Panels": ViewModel.PanelsVM.SaveAllCells(); break;
                case "Reactions": ViewModel.ReactionsVM.SaveAnalyzeCommand.Execute(null); break;
                default: ViewModel.SetStatus("No save action for this tab."); break;
            }
        }

        private void NewItem()
        {
            switch ((MainTabControl.SelectedItem as TabItem)?.Header as string)
            {
                case "Specimens": ViewModel.SpecimensVM.AddCommand.Execute(null); break;
                case "Panels": ViewModel.PanelsVM.AddCommand.Execute(null); break;
                case "Rules": ViewModel.RulesVM.AddCommand.Execute(null); break;
                default: ViewModel.SetStatus("No new item action for this tab."); break;
            }
        }

        private void ShowSettings()
        {
            var dlg = new Views.Dialogs.SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ViewModel.SetStatus("Preferences saved.");
                ViewModel.NotifyCapacityIfNeeded();
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
                "  0–4     write grade (0, 1+, 2+, 3+, 4+)\n" +
                "  N       write NT\n" +
                "  Enter   next phase, then next cell\n\n" +
                "Panel Antigen Grid:\n" +
                "  Press Edit to enter antigen edit mode\n" +
                "  Click a cell, or press Enter / Space, to toggle + and −\n" +
                "  Green = antigen present (+), light gray = antigen absent (−)\n" +
                "  Save or Cancel to leave edit mode",
                "Keyboard Shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadDemoData()
        {
            if (MessageBox.Show(
                    "This will add sample specimens, panels, and results if they are not already present.\n" +
                    "Existing data will not be removed. Continue?",
                    "Load Demo Data", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            try
            {
                ClinicalDataSeeder.SeedIfNeeded(ViewModel.Database, ViewModel.Analyzer);
                DemoDataSeeder.Seed(ViewModel.Database);
                ViewModel.RefreshAll();
                ViewModel.SetStatus("Sample workload and demo scenarios loaded.");
                MessageBox.Show(
                    "Sample data loaded.\n\n" +
                    "• 10 clinical specimens (2026-001 … 2026-010) on 5 shared panels\n" +
                    "• Enzyme (ficin) and absorption runs mixed in\n" +
                    "• 7 DEMO- scenarios for special-panel walkthroughs",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to seed demo data:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
