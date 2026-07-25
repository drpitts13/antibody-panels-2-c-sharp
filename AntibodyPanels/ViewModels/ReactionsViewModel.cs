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
        public ObservableCollection<PanelRun> Runs { get; } = new();
        public ObservableCollection<ReactionRow> Rows { get; } = new();

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set
            {
                if (SetField(ref _selectedSpecimen, value))
                {
                    RefreshPanels();
                    RefreshRuledOutAntigens();
                }
            }
        }

        private Panel? _selectedPanel;
        public Panel? SelectedPanel
        {
            get => _selectedPanel;
            set
            {
                if (SetField(ref _selectedPanel, value))
                    RefreshRuns();
            }
        }

        private PanelRun? _selectedRun;
        public PanelRun? SelectedRun
        {
            get => _selectedRun;
            set => SetField(ref _selectedRun, value);
        }

        private string _specimenFilter = string.Empty;
        public string SpecimenFilter
        {
            get => _specimenFilter;
            set { SetField(ref _specimenFilter, value); ApplySpecimenFilter(); }
        }

        /// <summary>Antigen names ruled out for the current specimen (column header colouring).</summary>
        private HashSet<string> _ruledOutAntigens = new();
        public HashSet<string> RuledOutAntigens
        {
            get => _ruledOutAntigens;
            private set => SetField(ref _ruledOutAntigens, value);
        }

        /// <summary>Antigens destroyed by the currently selected run's cell treatment.</summary>
        private HashSet<string> _destroyedAntigens = new();
        public HashSet<string> DestroyedAntigens
        {
            get => _destroyedAntigens;
            private set => SetField(ref _destroyedAntigens, value);
        }

        /// <summary>Phases suppressed by the currently selected run's serum treatment.</summary>
        private HashSet<string> _nonInterpretablePhases = new();
        public HashSet<string> NonInterpretablePhases
        {
            get => _nonInterpretablePhases;
            private set => SetField(ref _nonInterpretablePhases, value);
        }

        private string _treatmentBannerText = string.Empty;
        public string TreatmentBannerText
        {
            get => _treatmentBannerText;
            private set
            {
                if (SetField(ref _treatmentBannerText, value))
                    OnPropertyChanged(nameof(TreatmentBannerVisibility));
            }
        }

        public Visibility TreatmentBannerVisibility =>
            string.IsNullOrEmpty(_treatmentBannerText) ? Visibility.Collapsed : Visibility.Visible;

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
        public ICommand AddRunCommand { get; }
        public ICommand DeleteRunCommand { get; }

        public static string[] ReactionValues => AntigenConstants.ReactionValues.ToArray();

        public ReactionsViewModel(DatabaseService db, AntibodyAnalyzer analyzer, MainViewModel main)
        {
            _db = db;
            _analyzer = analyzer;
            _main = main;

            LoadCommand = new RelayCommand(LoadReactions,
                () => SelectedSpecimen != null && SelectedRun != null);
            SaveAnalyzeCommand = new RelayCommand(SaveAndAnalyze,
                () => SelectedSpecimen != null && SelectedRun != null && Rows.Count > 0);
            ClearCommand = new RelayCommand(ClearReactions,
                () => SelectedSpecimen != null && SelectedRun != null);
            AddRunCommand = new RelayCommand(AddRun,
                () => SelectedSpecimen != null && SelectedPanel != null);
            DeleteRunCommand = new RelayCommand(DeleteRun,
                () => SelectedRun != null && !(SelectedRun?.IsUntreated ?? true));

            RefreshSpecimens();
        }

        public void RefreshSpecimens()
        {
            var s = SelectedSpecimen?.AccessionNumber;
            Specimens.Clear();
            foreach (var sp in _db.GetActiveSpecimens()) Specimens.Add(sp);
            SelectedSpecimen = Specimens.FirstOrDefault(x => x.AccessionNumber == s);
        }

        private void RefreshPanels()
        {
            var pid = SelectedPanel?.PanelId;
            Panels.Clear();
            if (SelectedSpecimen == null) return;

            // Show only panels linked to this specimen
            foreach (var p in _db.GetSpecimenPanels(SelectedSpecimen.AccessionNumber))
                Panels.Add(p);

            SelectedPanel = Panels.FirstOrDefault(p => p.PanelId == pid)
                ?? Panels.FirstOrDefault();
        }

        private void RefreshRuns()
        {
            var rid = SelectedRun?.RunId;
            Runs.Clear();

            if (SelectedSpecimen == null || SelectedPanel == null)
            {
                SelectedRun = null;
                UpdateTreatmentBanner();
                return;
            }

            // Ensure the default (untreated) run exists
            _db.GetOrCreateDefaultRun(SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId);

            foreach (var r in _db.GetPanelRuns(SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId))
                Runs.Add(r);

            SelectedRun = Runs.FirstOrDefault(r => r.RunId == rid) ?? Runs.FirstOrDefault();
            UpdateTreatmentBanner();
        }

        private void UpdateTreatmentBanner()
        {
            if (SelectedRun == null || SelectedRun.IsUntreated)
            {
                TreatmentBannerText = string.Empty;
                DestroyedAntigens = new HashSet<string>();
                NonInterpretablePhases = new HashSet<string>();
                return;
            }

            var parts = new List<string> { SelectedRun.DisplayLabel };

            var destroyed = AntigenConstants.Antigens
                .Where(ag => AntigenTreatmentEffects.IsAntigenDestroyedOnCell(
                    SelectedRun.CellTreatment, ag))
                .ToList();
            if (destroyed.Count > 0)
                parts.Add($"Destroyed antigens: {string.Join(", ", destroyed)}");

            var suppressedPhases = AntigenTreatmentEffects
                .GetNonInterpretablePhases(SelectedRun.SerumTreatment).ToList();
            if (suppressedPhases.Count > 0)
                parts.Add($"Non-interpretable phases: {string.Join(", ", suppressedPhases)}");

            TreatmentBannerText = string.Join("  |  ", parts);

            DestroyedAntigens = new HashSet<string>(destroyed);
            NonInterpretablePhases = new HashSet<string>(suppressedPhases);
        }

        public void RefreshRuledOutAntigens()
        {
            if (SelectedSpecimen == null) { RuledOutAntigens = new(); return; }

            var allReactions = _db.GetAllSpecimenReactions(SelectedSpecimen.AccessionNumber);
            var rules = _db.GetAllRules();
            var result = new HashSet<string>();

            var byRun = new Dictionary<int, List<Reaction>>();
            foreach (var r in allReactions)
            {
                if (!byRun.ContainsKey(r.RunId)) byRun[r.RunId] = new();
                byRun[r.RunId].Add(r);
            }

            foreach (var (runId, runRxns) in byRun)
            {
                // We need the run's treatment to gate rule-outs
                var run = _db.GetPanelRun(runId);
                if (run == null) continue;
                var ctx = new RunContext(run);

                var cellDict = _db.GetPanelCells(run.PanelId).ToDictionary(c => c.CellNumber);
                foreach (var rxn in runRxns)
                {
                    if (rxn.CellNumber == "AC" || !ctx.IsNegative(rxn)) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in AntigenConstants.Antigens)
                    {
                        if (cell.GetAntigen(ag) != "+") continue;
                        if (!ctx.CanContributeRuleout(ag, cell)) continue;
                        if (CanRuleOutAntigen(ag, cell, rules)) result.Add(ag);
                    }
                }
            }
            RuledOutAntigens = result;
        }

        private static bool CanRuleOutAntigen(string antigen, PanelCell cell, List<Rule> rules)
        {
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;
            if (cell.GetAntigen(antithetical) == "-") return true;
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

        private void ApplySpecimenFilter() { }

        private void LoadReactions()
        {
            if (SelectedSpecimen == null || SelectedRun == null) return;

            if (SelectedSpecimen.IsActive == false || SelectedPanel?.IsActive == false)
            {
                var inactiveItem = SelectedSpecimen.IsActive == false ? "specimen" : "panel";
                MessageBox.Show(
                    $"Cannot load reactions: the selected {inactiveItem} is inactive. " +
                    "Reactivate it first if you need to work with its reactions.",
                    "Inactive Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveStatusMessage = string.Empty;
            Rows.Clear();

            var cells = _db.GetPanelCells(SelectedRun.PanelId);
            var reactions = _db.GetReactions(SelectedRun.RunId)
                .ToDictionary(r => r.CellNumber);
            var rules = _db.GetAllRules();
            var ctx = new RunContext(SelectedRun);

            foreach (var cell in cells)
            {
                reactions.TryGetValue(cell.CellNumber, out var rxn);
                Rows.Add(new ReactionRow(cell, rxn, rules, ctx));
            }

            UpdateTreatmentBanner();
            RefreshRuledOutAntigens();
            _main.SetStatus(
                $"Loaded {Rows.Count} cells for {SelectedSpecimen.AccessionNumber} / " +
                $"{SelectedRun.PanelName} ({SelectedRun.DisplayLabel})");
        }

        private void SaveAndAnalyze()
        {
            if (SelectedSpecimen == null || SelectedRun == null) return;

            if (SelectedSpecimen.IsActive == false || SelectedPanel?.IsActive == false)
            {
                var inactiveItem = SelectedSpecimen.IsActive == false ? "specimen" : "panel";
                MessageBox.Show(
                    $"Cannot save reactions: the selected {inactiveItem} is inactive. " +
                    "Reactivate it first if you need to work with its reactions.",
                    "Inactive Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                foreach (var row in Rows)
                    _db.SaveReaction(SelectedRun.RunId, row.CellNumber,
                        row.IS, row.C37, row.AHG, row.CC);

                _main.SetStatus("Reactions saved. Running analysis...");
                var result = _analyzer.AnalyzeSpecimen(SelectedSpecimen.AccessionNumber);
                RefreshRuledOutAntigens();
                var msg = $"Analysis complete — {result.Suspected.Count} suspected, " +
                          $"{result.RuledOut.Count} ruled out.";
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
            if (SelectedSpecimen == null || SelectedRun == null) return;
            if (MessageBox.Show("Clear all reactions for this run?",
                "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _db.DeleteReactions(SelectedRun.RunId);
            SaveStatusMessage = string.Empty;
            Rows.Clear();
            RefreshRuledOutAntigens();
            _main.SetStatus("Reactions cleared.");
        }

        private void AddRun()
        {
            if (SelectedSpecimen == null || SelectedPanel == null) return;
            var dlg = new Views.Dialogs.PanelRunDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var runId = _db.AddPanelRun(
                    SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId,
                    dlg.SelectedCellTreatment, dlg.SelectedSerumTreatment,
                    dlg.RunLabel);
                RefreshRuns();
                SelectedRun = Runs.FirstOrDefault(r => r.RunId == runId);
                _main.SetStatus($"Added run: {SelectedRun?.DisplayLabel}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not add run:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteRun()
        {
            if (SelectedRun == null || SelectedRun.IsUntreated)
            {
                MessageBox.Show("The untreated (default) run cannot be deleted.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Delete run '{SelectedRun.DisplayLabel}' and all its reactions?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            _db.DeletePanelRun(SelectedRun.RunId);
            Rows.Clear();
            RefreshRuns();
            _main.SetStatus("Run deleted.");
        }
    }

    public class ReactionRow : BaseViewModel
    {
        private readonly IReadOnlyDictionary<string, string> _antigens;
        private readonly IReadOnlyList<Rule> _rules;
        private readonly RunContext _ctx;

        public string CellNumber { get; }
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

        /// <summary>
        /// True when all interpretable phases are non-reactive.
        /// CC is a check-cell control, not a reactivity phase.
        /// </summary>
        public bool IsNegative => AHG == "0" && IsNtOrZero(IS) && IsNtOrZero(C37);

        public string RuledOutNote
        {
            get
            {
                if (!IsNegative) return string.Empty;
                var list = new List<string>();
                foreach (var ag in AntigenConstants.Antigens)
                {
                    if (!_antigens.TryGetValue(ag, out var v) || v != "+") continue;
                    // Skip antigens destroyed by the cell treatment
                    if (_ctx.GetAntigenEffect(ag) == AntigenEffect.Destroyed) continue;
                    if (CanRuleOut(ag)) list.Add($"anti-{ag}");
                }
                return string.Join(", ", list);
            }
        }

        public bool HasRuleout => !string.IsNullOrEmpty(RuledOutNote);

        public ReactionRow(PanelCell cell, Reaction? existing, IReadOnlyList<Rule> rules, RunContext ctx)
        {
            CellNumber = cell.CellNumber;
            _antigens = cell.Antigens;
            _rules = rules;
            _ctx = ctx;
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
            if (antitheticalVal == "-") return true;
            return ReactionsViewModel.RuleAllowsHeterozygous(antigen, _rules);
        }

        private static bool IsNtOrZero(string v) => v == "NT" || v == "0" || string.IsNullOrEmpty(v);
    }
}
