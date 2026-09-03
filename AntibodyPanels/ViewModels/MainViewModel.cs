using System;
using System.Linq;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        public DatabaseService Database { get; }
        public AntibodyAnalyzer Analyzer { get; }

        public WorklistViewModel WorklistVM { get; }
        public SpecimensViewModel SpecimensVM { get; }
        public PanelsViewModel PanelsVM { get; }
        public ReactionsViewModel ReactionsVM { get; }
        public AnalysisViewModel AnalysisVM { get; }
        public ReportsViewModel ReportsVM { get; }
        public SearchViewModel SearchVM { get; }
        public RulesViewModel RulesVM { get; }

        // Set by MainWindow after construction
        public ICommand? ExitCommand { get; set; }
        public ICommand? SaveCurrentCommand { get; set; }
        public ICommand? RefreshAllCommand { get; set; }
        public ICommand? NewItemCommand { get; set; }
        public ICommand? ShowShortcutsCommand { get; set; }
        public ICommand? ShowAboutCommand { get; set; }
        public ICommand? LoadDemoDataCommand { get; set; }
        public ICommand? ShowSettingsCommand { get; set; }
        public ICommand? ShowPurgeDatabaseCommand { get; set; }
        public ICommand? ShowOpenArchiveCommand { get; set; }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetField(ref _selectedTabIndex, value);
        }

        public MainViewModel()
        {
            SettingsService.Load();
            Database = new DatabaseService();
            Analyzer = new AntibodyAnalyzer(Database);
            ClinicalDataSeeder.SeedIfNeeded(Database, Analyzer);

            WorklistVM = new WorklistViewModel(Database, this);
            SpecimensVM = new SpecimensViewModel(Database, this);
            PanelsVM = new PanelsViewModel(Database, this);
            ReactionsVM = new ReactionsViewModel(Database, Analyzer, this);
            AnalysisVM = new AnalysisViewModel(Database, Analyzer, this);
            ReportsVM = new ReportsViewModel(Database, this);
            SearchVM = new SearchViewModel(Database, this);
            RulesVM = new RulesViewModel(Database, this);

            SpecimensVM.ShowInactive = AppSettings.Current.ShowInactiveByDefault;
            PanelsVM.ShowInactive = AppSettings.Current.ShowInactiveByDefault;
            ApplyDatabaseSizeLimit();
            AppSettings.Changed += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            SpecimensVM.ShowInactive = AppSettings.Current.ShowInactiveByDefault;
            PanelsVM.ShowInactive = AppSettings.Current.ShowInactiveByDefault;
            ReactionsVM.ApplyColumnVisibilitySettings();
            WorklistVM.Refresh();
            ApplyDatabaseSizeLimit();
            NotifyCapacityIfNeeded();
        }

        public DatabaseCapacityStatus GetCapacityStatus()
        {
            var maxBytes = DatabaseCapacityStatus.BytesFromMb(AppSettings.Current.MaxDatabaseSizeMb);
            return Database.GetCapacityStatus(maxBytes);
        }

        public bool IsDatabaseNearCapacity => GetCapacityStatus().IsNearCapacity;

        public void ApplyDatabaseSizeLimit()
        {
            var maxBytes = DatabaseCapacityStatus.BytesFromMb(AppSettings.Current.MaxDatabaseSizeMb);
            Database.ApplyMaxPageCount(maxBytes);
        }

        public void NotifyCapacityIfNeeded()
        {
            var cap = GetCapacityStatus();
            if (!cap.IsNearCapacity) return;
            SetStatus(
                $"Database {cap.PercentUsed:0}% full ({DatabaseCapacityStatus.FormatBytes(cap.FileBytes)} / {DatabaseCapacityStatus.FormatBytes(cap.MaxBytes)})");
        }

        public void SetStatus(string message) => StatusText = message;

        public void RefreshAll()
        {
            WorklistVM.Refresh();
            SpecimensVM.Refresh();
            PanelsVM.Refresh();
            ReactionsVM.RefreshSpecimens();
            AnalysisVM.Refresh();
            ReportsVM.Refresh();
            RulesVM.Refresh();
            SetStatus("All tabs refreshed");
        }

        public void NavigateToWorklistItem(WorklistItem item)
        {
            switch (item.TargetTab)
            {
                case "Reactions":
                    OpenSpecimenReactions(item.AccessionNumber);
                    break;
                case "Analysis":
                    OpenSpecimenAnalysis(item.AccessionNumber);
                    break;
                case "Panels":
                    SelectedTabIndex = 2;
                    if (item.PanelId is int pid)
                        PanelsVM.SelectPanel(pid);
                    break;
                default:
                    SelectedTabIndex = 1;
                    if (item.AccessionNumber != null)
                        SpecimensVM.SelectSpecimen(item.AccessionNumber);
                    break;
            }
        }

        public void OpenSpecimenReactions(string? accessionNumber)
        {
            SelectedTabIndex = 3;
            if (accessionNumber != null)
                ReactionsVM.SelectSpecimen(accessionNumber);
        }

        public void OpenSpecimenAnalysis(string? accessionNumber)
        {
            SelectedTabIndex = 4;
            if (accessionNumber != null)
                AnalysisVM.SelectSpecimen(accessionNumber);
        }

        public void OpenSpecimenReport(string? accessionNumber, string reportType = "Clinical Identification")
        {
            SelectedTabIndex = 5;
            if (accessionNumber != null)
                ReportsVM.SelectSpecimen(accessionNumber, reportType);
        }

        public void Dispose()
        {
            AppSettings.Changed -= OnSettingsChanged;
            Database?.Dispose();
        }
    }
}
