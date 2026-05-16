using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class ReactionsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly AntibodyAnalyzer _analyzer;
        private readonly MainViewModel _main;

        public ObservableCollection<Specimen> Specimens { get; } = new();
        public ObservableCollection<Panel> Panels { get; } = new();
        public ObservableCollection<ReactionRow> Rows { get; } = new();

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set { if (SetField(ref _selectedSpecimen, value)) RefreshRuledOutAntigens(); }
        }

        private Panel? _selectedPanel;
        public Panel? SelectedPanel
        {
            get => _selectedPanel;
            set => SetField(ref _selectedPanel, value);
        }

        private string _specimenFilter = string.Empty;
        public string SpecimenFilter
        {
            get => _specimenFilter;
            set { SetField(ref _specimenFilter, value); ApplySpecimenFilter(); }
        }

        /// <summary>
        /// Set of antigen names (e.g. "D", "K") that are ruled out for the current specimen
        /// across all attached panels. The view uses this to colour column headers red.
        /// </summary>
        private HashSet<string> _ruledOutAntigens = new();
        public HashSet<string> RuledOutAntigens
        {
            get => _ruledOutAntigens;
            private set => SetField(ref _ruledOutAntigens, value);
        }

        private string _saveStatusMessage = string.Empty;
        public string SaveStatusMessage
        {
            get => _saveStatusMessage;
            private set
            {
                if (SetField(ref _saveStatusMessage, value))
                    OnPropertyChanged(nameof(SaveStatusVisibility));
            }
        }

        private bool _saveStatusIsSuccess;
        public bool SaveStatusIsSuccess
        {
            get => _saveStatusIsSuccess;
            private set => SetField(ref _saveStatusIsSuccess, value);
        }

        public Visibility SaveStatusVisibility =>
            string.IsNullOrEmpty(_saveStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

        private void SetSaveStatus(bool success, string message)
        {
            SaveStatusIsSuccess = success;
            SaveStatusMessage = message;
        }

        public ICommand LoadCommand { get; }
        public ICommand SaveAnalyzeCommand { get; }
        public ICommand ClearCommand { get; }

        public static string[] ReactionValues => AntigenConstants.ReactionValues.ToArray();

        public ReactionsViewModel(DatabaseService db, AntibodyAnalyzer analyzer, MainViewModel main)
        {
            _db = db;
            _analyzer = analyzer;
            _main = main;

            LoadCommand = new RelayCommand(LoadReactions,
                () => SelectedSpecimen != null && SelectedPanel != null);
            SaveAnalyzeCommand = new RelayCommand(SaveAndAnalyze,
                () => SelectedSpecimen != null && SelectedPanel != null && Rows.Count > 0);
            ClearCommand = new RelayCommand(ClearReactions,
                () => SelectedSpecimen != null && SelectedPanel != null);

            RefreshSpecimens();
        }

        public void RefreshSpecimens()
        {
            var s = SelectedSpecimen?.AccessionNumber;
            Specimens.Clear();
            foreach (var sp in _db.GetAllSpecimens()) Specimens.Add(sp);
            Panels.Clear();
            foreach (var p in _db.GetAllPanels()) Panels.Add(p);
            if (s != null)
                SelectedSpecimen = Specimens.FirstOrDefault(x => x.AccessionNumber == s);
        }

        /// <summary>
        /// Recomputes which antigens are ruled out for the current specimen across
        /// ALL panels it has reactions on, then raises PropertyChanged so the view
        /// can update the column header colours.
        /// </summary>
        public void RefreshRuledOutAntigens()
        {
            if (SelectedSpecimen == null) { RuledOutAntigens = new(); return; }

            var allReactions = _db.GetAllSpecimenReactions(SelectedSpecimen.AccessionNumber);
            var rules = _db.GetAllRules();
            var result = new HashSet<string>();

            // Group reactions by panel
            var byPanel = new Dictionary<int, List<Reaction>>();
            foreach (var r in allReactions)
            {
                if (!byPanel.ContainsKey(r.PanelId)) byPanel[r.PanelId] = new();
                byPanel[r.PanelId].Add(r);
            }

            foreach (var (panelId, panelRxns) in byPanel)
            {
                var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                foreach (var rxn in panelRxns)
                {
                    if (rxn.CellNumber == "AC" || !rxn.IsNegative) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in AntigenConstants.Antigens)
                    {
                        if (cell.GetAntigen(ag) != "+") continue;
                        if (CanRuleOutAntigen(ag, cell, rules))
                            result.Add(ag);
                    }
                }
            }

            RuledOutAntigens = result;
        }

        private static bool CanRuleOutAntigen(string antigen, PanelCell cell, List<Rule> rules)
        {
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;
            if (cell.GetAntigen(antithetical) == "-") return true; // homozygous
            return RuleAllowsHeterozygous(antigen, rules);
        }

        internal static bool RuleAllowsHeterozygous(string antigen, IReadOnlyList<Rule> rules)
        {
            foreach (var rule in rules)
            {
                if (!rule.HeterozygousOk) continue;
                if (rule.ExceptionAntigen == antigen) return true;
                if (string.IsNullOrEmpty(rule.ExceptionAntigen) &&
                    string.Equals(rule.Antibody, $"anti-{antigen}", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void ApplySpecimenFilter()
        {
            // Filtering is done in the View via CollectionView; ViewModel just needs to raise prop-change.
        }

        private void LoadReactions()
        {
            if (SelectedSpecimen == null || SelectedPanel == null) return;
            SaveStatusMessage = string.Empty;
            Rows.Clear();

            var cells = _db.GetPanelCells(SelectedPanel.PanelId);
            var reactions = _db.GetReactions(SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId)
                .ToDictionary(r => r.CellNumber);
            var rules = _db.GetAllRules();

            foreach (var cell in cells)
            {
                reactions.TryGetValue(cell.CellNumber, out var rxn);
                Rows.Add(new ReactionRow(cell, rxn, rules));
            }

            RefreshRuledOutAntigens();
            _main.SetStatus($"Loaded {Rows.Count} cells for {SelectedSpecimen.AccessionNumber} / {SelectedPanel.Name}");
        }

        private void SaveAndAnalyze()
        {
            if (SelectedSpecimen == null || SelectedPanel == null) return;
            try
            {
                foreach (var row in Rows)
                    _db.SaveReaction(SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId,
                        row.CellNumber, row.IS, row.C37, row.AHG, row.CC);

                _main.SetStatus("Reactions saved. Running analysis...");

                var result = _analyzer.AnalyzeSpecimen(SelectedSpecimen.AccessionNumber);
                RefreshRuledOutAntigens();
                var msg = $"Analysis complete — {result.Suspected.Count} suspected, {result.RuledOut.Count} ruled out.";
                _main.SetStatus(msg);
                _main.SpecimensVM.Refresh();
                SetSaveStatus(true, msg);
            }
            catch (Exception ex)
            {
                var msg = $"Save & Analyze failed: {ex.Message}";
                _main.SetStatus(msg);
                SetSaveStatus(false, msg);
            }
        }

        private void ClearReactions()
        {
            if (SelectedSpecimen == null || SelectedPanel == null) return;
            if (MessageBox.Show("Clear all reactions for this specimen/panel?",
                "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _db.DeleteReactions(SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId);
            SaveStatusMessage = string.Empty;
            Rows.Clear();
            RefreshRuledOutAntigens();
            _main.SetStatus("Reactions cleared.");
        }
    }

    public class ReactionRow : BaseViewModel
    {
        private readonly IReadOnlyDictionary<string, string> _antigens;
        private readonly IReadOnlyList<Rule> _rules;

        public string CellNumber { get; }

        // Antigen profile for this cell (used by column bindings and rule-out computation)
        public IReadOnlyDictionary<string, string> AntigenValues => _antigens;

        private string _IS;
        public string IS
        {
            get => _IS;
            set { if (SetField(ref _IS, value)) NotifyRuleout(); }
        }

        private string _C37;
        public string C37
        {
            get => _C37;
            set { if (SetField(ref _C37, value)) NotifyRuleout(); }
        }

        private string _AHG;
        public string AHG
        {
            get => _AHG;
            set { if (SetField(ref _AHG, value)) NotifyRuleout(); }
        }

        private string _CC;
        public string CC
        {
            get => _CC;
            set { if (SetField(ref _CC, value)) NotifyRuleout(); }
        }

        // True when the reaction phases indicate a negative result
        public bool IsNegative =>
            (AHG == "0" && IsNtOrZero(IS) && IsNtOrZero(C37) && IsNtOrZero(CC)) ||
            (IS == "0" && C37 == "0" && AHG == "0" && CC == "0");

        // Comma-separated list of antibodies ruled out by this cell
        public string RuledOutNote
        {
            get
            {
                if (!IsNegative) return string.Empty;
                var list = new System.Collections.Generic.List<string>();
                foreach (var ag in AntigenConstants.Antigens)
                    if (_antigens.TryGetValue(ag, out var v) && v == "+" && CanRuleOut(ag))
                        list.Add($"anti-{ag}");
                return string.Join(", ", list);
            }
        }

        public bool HasRuleout => !string.IsNullOrEmpty(RuledOutNote);

        public ReactionRow(PanelCell cell, Reaction? existing, IReadOnlyList<Rule> rules)
        {
            CellNumber = cell.CellNumber;
            _antigens = cell.Antigens;
            _rules = rules;
            _IS  = existing?.IS  ?? "NT";
            _C37 = existing?.C37 ?? "NT";
            _AHG = existing?.AHG ?? "NT";
            _CC  = existing?.CC  ?? "NT";
        }

        private void NotifyRuleout()
        {
            OnPropertyChanged(nameof(IsNegative));
            OnPropertyChanged(nameof(RuledOutNote));
            OnPropertyChanged(nameof(HasRuleout));
        }

        private bool CanRuleOut(string antigen)
        {
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;
            var antitheticalVal = _antigens.TryGetValue(antithetical, out var av) ? av : "-";
            if (antitheticalVal == "-") return true; // homozygous
            return ReactionsViewModel.RuleAllowsHeterozygous(antigen, _rules);
        }

        private static bool IsNtOrZero(string v) => v == "NT" || v == "0" || string.IsNullOrEmpty(v);
    }
}
