using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.ViewModels
{
    public class SearchViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel _main;

        public ObservableCollection<AntigenCriterionRow> Criteria { get; } = new();
        public ObservableCollection<SearchResultRow> Results { get; } = new();

        public ICommand SearchCommand { get; }
        public ICommand ClearCommand { get; }

        public SearchViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;

            foreach (var ag in AntigenConstants.Antigens)
                Criteria.Add(new AntigenCriterionRow(ag));

            SearchCommand = new RelayCommand(RunSearch);
            ClearCommand = new RelayCommand(ClearCriteria);
        }

        private void RunSearch()
        {
            var criteria = Criteria
                .Where(c => c.Selected != "Any")
                .ToDictionary(c => c.Antigen, c => c.Selected);

            if (criteria.Count == 0)
            {
                _main.SetStatus("Please select at least one antigen criterion.");
                return;
            }

            var matches = _db.SearchCellsByProfile(criteria);
            Results.Clear();
            foreach (var (panel, cell) in matches)
                Results.Add(new SearchResultRow(panel, cell));

            _main.SetStatus($"Found {Results.Count} matching cells for {criteria.Count} criteria.");
        }

        private void ClearCriteria()
        {
            foreach (var c in Criteria) c.Selected = "Any";
            Results.Clear();
            _main.SetStatus("Search criteria cleared.");
        }
    }

    public class AntigenCriterionRow : BaseViewModel
    {
        public string Antigen { get; }
        public static string[] Options => new[] { "Any", "+", "-" };

        private string _selected = "Any";
        public string Selected
        {
            get => _selected;
            set => SetField(ref _selected, value);
        }

        public AntigenCriterionRow(string antigen) => Antigen = antigen;
    }

    public class SearchResultRow
    {
        public string PanelName { get; }
        public string? LotNumber { get; }
        public string CellNumber { get; }

        // Antigen values
        public string D { get; }
        public string C { get; }
        public string c { get; }
        public string E { get; }
        public string e { get; }
        public string f { get; }
        public string Cw { get; }
        public string V { get; }
        public string K { get; }
        public string k { get; }
        public string Kpa { get; }
        public string Kpb { get; }
        public string Jsa { get; }
        public string Jsb { get; }
        public string Jka { get; }
        public string Jkb { get; }
        public string Fya { get; }
        public string Fyb { get; }
        public string Lea { get; }
        public string Leb { get; }
        public string M { get; }
        public string N { get; }
        public string S { get; }
        public string s { get; }
        public string Lua { get; }
        public string Lub { get; }
        public string Xga { get; }
        public string P1 { get; }

        public SearchResultRow(Panel panel, PanelCell cell)
        {
            PanelName = panel.Name;
            LotNumber = panel.LotNumber;
            CellNumber = cell.CellNumber;
            D = cell.GetAntigen("D"); C = cell.GetAntigen("C"); c = cell.GetAntigen("c");
            E = cell.GetAntigen("E"); e = cell.GetAntigen("e"); f = cell.GetAntigen("f");
            Cw = cell.GetAntigen("Cw"); V = cell.GetAntigen("V");
            K = cell.GetAntigen("K"); k = cell.GetAntigen("k");
            Kpa = cell.GetAntigen("Kpa"); Kpb = cell.GetAntigen("Kpb");
            Jsa = cell.GetAntigen("Jsa"); Jsb = cell.GetAntigen("Jsb");
            Jka = cell.GetAntigen("Jka"); Jkb = cell.GetAntigen("Jkb");
            Fya = cell.GetAntigen("Fya"); Fyb = cell.GetAntigen("Fyb");
            Lea = cell.GetAntigen("Lea"); Leb = cell.GetAntigen("Leb");
            M = cell.GetAntigen("M"); N = cell.GetAntigen("N");
            S = cell.GetAntigen("S"); s = cell.GetAntigen("s");
            Lua = cell.GetAntigen("Lua"); Lub = cell.GetAntigen("Lub");
            Xga = cell.GetAntigen("Xga"); P1 = cell.GetAntigen("P1");
        }
    }
}
