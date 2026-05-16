using System;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        public DatabaseService Database { get; }
        public AntibodyAnalyzer Analyzer { get; }

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

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public MainViewModel()
        {
            Database = new DatabaseService();
            Analyzer = new AntibodyAnalyzer(Database);

            SpecimensVM = new SpecimensViewModel(Database, this);
            PanelsVM = new PanelsViewModel(Database, this);
            ReactionsVM = new ReactionsViewModel(Database, Analyzer, this);
            AnalysisVM = new AnalysisViewModel(Database, Analyzer, this);
            ReportsVM = new ReportsViewModel(Database, this);
            SearchVM = new SearchViewModel(Database, this);
            RulesVM = new RulesViewModel(Database, this);
        }

        public void SetStatus(string message) => StatusText = message;

        public void RefreshAll()
        {
            SpecimensVM.Refresh();
            PanelsVM.Refresh();
            ReactionsVM.RefreshSpecimens();
            RulesVM.Refresh();
            SetStatus("All tabs refreshed");
        }

        public void Dispose() => Database?.Dispose();
    }
}
