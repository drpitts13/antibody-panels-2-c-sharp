using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AntibodyPanels.Help;
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
            vm.ShowManualCommand = new RelayCommand(HelpService.ShowManual);
            vm.ShowShortcutsCommand = new RelayCommand(ShowShortcuts);
            BuildHelpTopicsMenu();
            vm.ShowAboutCommand = new RelayCommand(ShowAbout);
            vm.LoadDemoDataCommand = new RelayCommand(LoadDemoData);
            vm.ShowSettingsCommand = new RelayCommand(ShowSettings);
            vm.ShowPurgeDatabaseCommand = new RelayCommand(ShowPurgeDatabase);
            vm.ShowOpenArchiveCommand = new RelayCommand(ShowOpenArchive);
            vm.ShowAuditLogCommand = new RelayCommand(ShowAuditLog);

            Loaded += MainWindow_Loaded;

            Closing += (s, e) =>
            {
                if (MessageBox.Show("Quit the application?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    e.Cancel = true;
                else
                {
                    HelpService.Close();
                    vm.Dispose();
                }
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AcknowledgeIntendedUseIfNeeded();
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
                case "Reactions": ViewModel.ReactionsVM.SaveCommand.Execute(null); break;
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
            var dlg = new Views.Dialogs.SettingsDialog(ViewModel.Database) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ViewModel.SetStatus("Preferences saved.");
                ViewModel.NotifyCapacityIfNeeded();
            }
        }

        private void ShowShortcuts() => HelpService.Show(HelpCatalog.ShortcutsId);

        private void BuildHelpTopicsMenu()
        {
            HelpTopicsMenu.Items.Clear();
            foreach (var node in HelpCatalog.Tree())
                HelpTopicsMenu.Items.Add(CreateTopicMenu(node));
        }

        private static MenuItem CreateTopicMenu(HelpTopicNode node)
        {
            var item = new MenuItem { Header = node.Title.Replace("_", "__") };
            if (node.Children.Count == 0)
            {
                item.Command = HelpCommands.ShowTopic;
                item.CommandParameter = node.Id;
                return item;
            }

            item.Items.Add(new MenuItem
            {
                Header = node.Title.Replace("_", "__"),
                Command = HelpCommands.ShowTopic,
                CommandParameter = node.Id
            });
            foreach (var child in node.Children)
                item.Items.Add(CreateTopicMenu(child));
            return item;
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
                DemoDataSeeder.SeedIfNeeded(ViewModel.Database);
                ViewModel.RefreshAll();
                ViewModel.SetStatus("Sample workload and demo scenarios loaded.");
                MessageBox.Show(
                    "Sample data loaded.\n\n" +
                    "• 10 clinical specimens (2026-001 … 2026-010) on 5 shared panels\n" +
                    "• Enzyme (ficin) and absorption runs mixed in\n" +
                    "• 11 DEMO- scenarios including warehouse antigens with ficin/DTT runs",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to seed demo data:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowAuditLog()
        {
            var dlg = new Views.Dialogs.AuditLogDialog(ViewModel.Database) { Owner = this };
            dlg.ShowDialog();
        }

        private void AcknowledgeIntendedUseIfNeeded()
        {
            if (AppSettings.Current.IntendedUseAcknowledged) return;
            MessageBox.Show(SoftwareIdentity.IntendedUse, "Intended use",
                MessageBoxButton.OK, MessageBoxImage.Information);
            AppSettings.Current.IntendedUseAcknowledged = true;
            SettingsService.Save();
        }

        private void ShowAbout()
        {
            MessageBox.Show(SoftwareIdentity.AboutText(), "About",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
