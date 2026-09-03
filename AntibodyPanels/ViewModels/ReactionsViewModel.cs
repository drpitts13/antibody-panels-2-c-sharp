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
        public ObservableCollection<CompareReactionRow> CompareRows { get; } = new();

        private bool _gradesDirty;
        public bool HasUnsavedGrades => _gradesDirty;

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set
            {
                if (_selectedSpecimen?.AccessionNumber == value?.AccessionNumber)
                {
                    _selectedSpecimen = value;
                    return;
                }
                if (!ConfirmDiscardUnsaved())
                {
                    OnPropertyChanged();
                    return;
                }
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
                if (_selectedPanel?.PanelId == value?.PanelId)
                {
                    _selectedPanel = value;
                    return;
                }
                if (!ConfirmDiscardUnsaved())
                {
                    OnPropertyChanged();
                    return;
                }
                if (SetField(ref _selectedPanel, value))
                {
                    RefreshExtraAntigens();
                    RefreshRuns();
                }
            }
        }

        private PanelRun? _selectedRun;
        public PanelRun? SelectedRun
        {
            get => _selectedRun;
            set
            {
                if (_selectedRun?.RunId == value?.RunId)
                {
                    _selectedRun = value;
                    return;
                }
                if (!ConfirmDiscardUnsaved())
                {
                    OnPropertyChanged();
                    return;
                }
                if (SetField(ref _selectedRun, value))
                {
                    UpdateTreatmentBanner();
                    RefreshCompareRunChoices();
                    LoadReactions();
                }
            }
        }

        private bool _compareMode;
        public bool CompareMode
        {
            get => _compareMode;
            set
            {
                if (SetField(ref _compareMode, value))
                {
                    OnPropertyChanged(nameof(ComparePanelVisibility));
                    RefreshCompareRunChoices();
                    RebuildCompareRows();
                }
            }
        }

        public Visibility ComparePanelVisibility =>
            CompareMode ? Visibility.Visible : Visibility.Collapsed;

        private PanelRun? _compareRun;
        public PanelRun? CompareRun
        {
            get => _compareRun;
            set
            {
                if (SetField(ref _compareRun, value))
                    RebuildCompareRows();
            }
        }

        public ObservableCollection<PanelRun> CompareRunChoices { get; } = new();

        public bool HideRuledOutAntigenColumns
        {
            get => AppSettings.Current.HideRuledOutAntigenColumns;
            set
            {
                if (AppSettings.Current.HideRuledOutAntigenColumns == value) return;
                AppSettings.Current.HideRuledOutAntigenColumns = value;
                SettingsService.Save();
                ApplyColumnVisibilitySettings();
            }
        }

        private List<Specimen> _allSpecimens = new();
        private string _specimenFilter = string.Empty;
        public string SpecimenFilter
        {
            get => _specimenFilter;
            set { if (SetField(ref _specimenFilter, value)) ApplySpecimenFilter(); }
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

        private IReadOnlyList<string> _extraAntigens = Array.Empty<string>();
        public IReadOnlyList<string> ExtraAntigens
        {
            get => _extraAntigens;
            private set => SetField(ref _extraAntigens, value);
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

        public Visibility SaveStatusVisibility =>
            string.IsNullOrEmpty(_saveStatusMessage) ? Visibility.Collapsed : Visibility.Visible;

        private bool _saveStatusIsSuccess;
        public bool SaveStatusIsSuccess
        {
            get => _saveStatusIsSuccess;
            private set => SetField(ref _saveStatusIsSuccess, value);
        }

        public string EntryProgressText
        {
            get
            {
                if (Rows.Count == 0) return string.Empty;
                return FormatEntryProgress(Rows.Count(r => r.HasEnteredGrade), Rows.Count);
            }
        }

        public static string FormatEntryProgress(int entered, int total)
        {
            if (total <= 0) return string.Empty;
            if (entered >= total) return $"All {total} cells have grades.";
            return $"Grades entered: {entered} of {total} cells.";
        }

        private void RefreshEntryProgress() => OnPropertyChanged(nameof(EntryProgressText));

        private void OnGradeEdited()
        {
            RefreshEntryProgress();
            if (_gradesDirty) return;
            _gradesDirty = true;
            OnPropertyChanged(nameof(HasUnsavedGrades));
        }

        private void MarkGradesClean()
        {
            if (!_gradesDirty) return;
            _gradesDirty = false;
            OnPropertyChanged(nameof(HasUnsavedGrades));
        }

        public static bool NeedsDiscardPrompt(bool dirty, int? currentRunId, int? nextRunId) =>
            dirty && currentRunId != nextRunId;

        private bool ConfirmDiscardUnsaved()
        {
            if (!_gradesDirty) return true;
            var discard = MessageBox.Show(
                "Discard unsaved reaction grades?",
                "Unsaved grades", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (discard != MessageBoxResult.Yes) return false;
            MarkGradesClean();
            return true;
        }

        private void SetSaveStatus(bool success, string message)
        {
            SaveStatusIsSuccess = success;
            SaveStatusMessage = message;
        }

        public ICommand LoadCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAnalyzeCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand FillNegativesCommand { get; }
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
            SaveCommand = new RelayCommand(SaveGradesOnly,
                () => SelectedSpecimen != null && SelectedRun != null && Rows.Count > 0);
            SaveAnalyzeCommand = new RelayCommand(SaveAndAnalyze,
                () => SelectedSpecimen != null && SelectedRun != null && Rows.Count > 0);
            ClearCommand = new RelayCommand(ClearReactions,
                () => SelectedSpecimen != null && SelectedRun != null);
            FillNegativesCommand = new RelayCommand(FillRemainingNegatives,
                () => Rows.Count > 0);
            AddRunCommand = new RelayCommand(AddRun,
                () => SelectedSpecimen != null && SelectedPanel != null);
            DeleteRunCommand = new RelayCommand(DeleteRun,
                () => SelectedRun != null && !(SelectedRun?.IsUntreated ?? true));

            RefreshSpecimens();
        }

        public void ApplyColumnVisibilitySettings()
        {
            OnPropertyChanged(nameof(HideRuledOutAntigenColumns));
            OnPropertyChanged(nameof(RuledOutAntigens));
        }

        public void SelectSpecimen(string accessionNumber)
        {
            if (_allSpecimens.Count == 0)
                LoadAllSpecimens();
            var match = _allSpecimens.FirstOrDefault(x => x.AccessionNumber == accessionNumber);
            if (match != null && !match.MatchesFilter(_specimenFilter))
            {
                _specimenFilter = string.Empty;
                OnPropertyChanged(nameof(SpecimenFilter));
            }
            ApplySpecimenFilter(accessionNumber);
        }

        public void RefreshSpecimens()
        {
            var s = SelectedSpecimen?.AccessionNumber;
            LoadAllSpecimens();
            ApplySpecimenFilter(s);
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
            RefreshCompareRunChoices();
        }

        private void RefreshCompareRunChoices()
        {
            var keep = CompareRun?.RunId;
            CompareRunChoices.Clear();
            foreach (var r in Runs)
            {
                if (SelectedRun != null && r.RunId == SelectedRun.RunId) continue;
                CompareRunChoices.Add(r);
            }
            CompareRun = CompareRunChoices.FirstOrDefault(r => r.RunId == keep)
                ?? CompareRunChoices.FirstOrDefault();
        }

        private void RebuildCompareRows()
        {
            CompareRows.Clear();
            if (!CompareMode || SelectedRun == null || CompareRun == null || Rows.Count == 0)
                return;

            var other = _db.GetReactions(CompareRun.RunId).ToDictionary(r => r.CellNumber);
            foreach (var row in Rows)
            {
                other.TryGetValue(row.CellNumber, out var rxn);
                CompareRows.Add(new CompareReactionRow
                {
                    CellNumber = row.CellNumber,
                    LeftIS = row.IS,
                    LeftC37 = row.C37,
                    LeftAHG = row.AHG,
                    LeftCC = row.CC,
                    RightIS = rxn?.IS ?? "NT",
                    RightC37 = rxn?.C37 ?? "NT",
                    RightAHG = rxn?.AHG ?? "NT",
                    RightCC = rxn?.CC ?? "NT",
                    LeftLabel = SelectedRun.DisplayLabel,
                    RightLabel = CompareRun.DisplayLabel,
                });
            }
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

            var destroyed = VisibleAntigens
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
                var ctx = CreateRunContext(run);

                var cellDict = _db.GetPanelCells(run.PanelId).ToDictionary(c => c.CellNumber);
                var antigens = AntigenConstants.GetAnalyzedAntigens(ctx.ExtraAntigens);
                foreach (var rxn in runRxns)
                {
                    if (rxn.CellNumber == "AC" || !ctx.IsNegative(rxn)) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in antigens)
                    {
                        if (!ctx.TypesAntigen(ag)) continue;
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
            if (!cell.HasTypedAntigen(antithetical))
                return false;
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

        private void RefreshExtraAntigens()
        {
            ExtraAntigens = SelectedPanel == null
                ? Array.Empty<string>()
                : _db.GetPanelExtraAntigens(SelectedPanel.PanelId);
        }

        private IReadOnlyList<string> VisibleAntigens =>
            AntigenConstants.GetAnalyzedAntigens(ExtraAntigens);

        private RunContext CreateRunContext(PanelRun run) =>
            new(run, _db.GetPanelExtraAntigens(run.PanelId));

        private void LoadAllSpecimens()
        {
            _allSpecimens = _db.GetActiveSpecimens();
        }

        private void ApplySpecimenFilter(string? preferredAccession = null)
        {
            preferredAccession ??= SelectedSpecimen?.AccessionNumber;
            Specimens.Clear();
            foreach (var sp in _allSpecimens.Where(s => s.MatchesFilter(_specimenFilter)))
                Specimens.Add(sp);
            SelectedSpecimen = Specimens.FirstOrDefault(x => x.AccessionNumber == preferredAccession)
                ?? Specimens.FirstOrDefault();
        }

        private void LoadReactions()
        {
            if (SelectedSpecimen == null || SelectedRun == null)
            {
                Rows.Clear();
                CompareRows.Clear();
                RefreshEntryProgress();
                MarkGradesClean();
                return;
            }

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
            var ctx = CreateRunContext(SelectedRun);

            foreach (var cell in cells)
            {
                reactions.TryGetValue(cell.CellNumber, out var rxn);
                Rows.Add(new ReactionRow(cell, rxn, rules, ctx, OnGradeEdited));
            }

            UpdateTreatmentBanner();
            RefreshRuledOutAntigens();
            RebuildCompareRows();
            RefreshEntryProgress();
            MarkGradesClean();
            _main.SetStatus(
                $"Loaded {Rows.Count} cells for {SelectedSpecimen.AccessionNumber} / " +
                $"{SelectedRun.PanelName} ({SelectedRun.DisplayLabel})");
        }

        private void SaveGradesOnly()
        {
            if (!TrySaveGrades(out var entered)) return;
            var msg = $"Saved grades for {entered} of {Rows.Count} cells.";
            _main.SetStatus(msg);
            SetSaveStatus(true, msg);
            _main.WorklistVM.Refresh();
        }

        private bool TrySaveGrades(out int entered)
        {
            entered = 0;
            if (SelectedSpecimen == null || SelectedRun == null) return false;

            if (SelectedSpecimen.IsActive == false || SelectedPanel?.IsActive == false)
            {
                var inactiveItem = SelectedSpecimen.IsActive == false ? "specimen" : "panel";
                MessageBox.Show(
                    $"Cannot save reactions: the selected {inactiveItem} is inactive. " +
                    "Reactivate it first if you need to work with its reactions.",
                    "Inactive Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                foreach (var row in Rows)
                    _db.SaveReaction(SelectedRun.RunId, row.CellNumber,
                        row.IS, row.C37, row.AHG, row.CC);
                entered = Rows.Count(r => r.HasEnteredGrade);
                MarkGradesClean();
                return true;
            }
            catch (Exception ex)
            {
                var msg = $"Save failed: {ex.Message}";
                _main.SetStatus(msg);
                SetSaveStatus(false, msg);
                return false;
            }
        }

        private void SaveAndAnalyze()
        {
            if (!TrySaveGrades(out _)) return;

            try
            {
                _main.SetStatus("Reactions saved. Running analysis...");
                var result = _analyzer.AnalyzeSpecimen(SelectedSpecimen!.AccessionNumber);
                RefreshRuledOutAntigens();
                var msg = $"Analysis complete — {result.Suspected.Count} suspected, " +
                          $"{result.RuledOut.Count} ruled out.";
                _main.SetStatus(msg);
                _main.SpecimensVM.Refresh();
                _main.WorklistVM.Refresh();
                RebuildCompareRows();
                SetSaveStatus(true, msg);
            }
            catch (Exception ex)
            {
                var msg = $"Save & Analyze failed: {ex.Message}";
                _main.SetStatus(msg);
                SetSaveStatus(false, msg);
            }
        }

        private void FillRemainingNegatives()
        {
            if (Rows.Count == 0) return;
            int changed = 0;
            foreach (var row in Rows)
            {
                if (row.FillRemainingNegatives())
                    changed++;
            }
            RebuildCompareRows();
            RefreshEntryProgress();
            if (changed == 0)
            {
                _main.SetStatus("No NT phases left to fill.");
                return;
            }
            var msg = $"Filled remaining NT phases as negative on {changed} cell(s). Save to keep.";
            _main.SetStatus(msg);
            SetSaveStatus(true, msg);
        }

        private void ClearReactions()
        {
            if (SelectedSpecimen == null || SelectedRun == null) return;
            if (MessageBox.Show("Clear all reactions for this run?",
                "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _db.DeleteReactions(SelectedRun.RunId);
            SaveStatusMessage = string.Empty;
            Rows.Clear();
            CompareRows.Clear();
            RefreshRuledOutAntigens();
            RefreshEntryProgress();
            MarkGradesClean();
            _main.SetStatus("Reactions cleared.");
        }

        private void AddRun()
        {
            if (SelectedSpecimen == null || SelectedPanel == null) return;
            var dlg = new Views.Dialogs.PanelRunDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            var sourceRun = SelectedRun;
            var inGridGrades = Rows.Select(r => (r.CellNumber, r.IS, r.C37, r.AHG, r.CC)).ToList();

            try
            {
                var runId = _db.AddPanelRun(
                    SelectedSpecimen.AccessionNumber, SelectedPanel.PanelId,
                    dlg.SelectedCellTreatment, dlg.SelectedSerumTreatment,
                    dlg.RunLabel);

                int copied = 0;
                if (dlg.CopyGradesFromCurrentRun)
                {
                    if (inGridGrades.Count > 0)
                    {
                        foreach (var g in inGridGrades)
                            _db.SaveReaction(runId, g.CellNumber, g.IS, g.C37, g.AHG, g.CC);
                        copied = inGridGrades.Count;
                    }
                    else if (sourceRun != null)
                    {
                        copied = _db.CopyReactions(sourceRun.RunId, runId);
                    }
                }

                RefreshRuns();
                SelectedRun = Runs.FirstOrDefault(r => r.RunId == runId);
                LoadReactions();
                _main.SetStatus(copied > 0
                    ? $"Added run: {SelectedRun?.DisplayLabel} (copied {copied} cell grades)."
                    : $"Added run: {SelectedRun?.DisplayLabel}");
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
        private readonly Action? _onGradeChanged;

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
                foreach (var ag in AntigenConstants.GetAnalyzedAntigens(
                             _antigens.Keys.Where(AntigenConstants.IsWarehouse)))
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

        public bool HasEnteredGrade =>
            IsGradeEntered(IS) || IsGradeEntered(C37) || IsGradeEntered(AHG);

        public bool IsIncomplete => !HasEnteredGrade;

        public static bool IsGradeEntered(string? v) =>
            !string.IsNullOrEmpty(v) && v != "NT";

        public static (string IS, string C37, string AHG, string CC) FillNegativeDefaults(
            string? isPhase, string? c37, string? ahg, string? cc)
        {
            var nis = IsGradeEntered(isPhase) ? isPhase! : "0";
            var nc37 = IsGradeEntered(c37) ? c37! : "0";
            var nahg = IsGradeEntered(ahg) ? ahg! : "0";
            var ncc = IsGradeEntered(cc) ? cc! : (nahg == "0" ? "2+" : "NT");
            return (nis, nc37, nahg, ncc);
        }

        public bool FillRemainingNegatives()
        {
            var filled = FillNegativeDefaults(IS, C37, AHG, CC);
            if (filled.IS == IS && filled.C37 == C37 && filled.AHG == AHG && filled.CC == CC)
                return false;
            IS = filled.IS;
            C37 = filled.C37;
            AHG = filled.AHG;
            CC = filled.CC;
            return true;
        }

        public bool IsCcInvalid =>
            AHG == "0" && (CC == "0" || CC == "NT" || string.IsNullOrEmpty(CC));

        public ReactionRow(PanelCell cell, Reaction? existing, IReadOnlyList<Rule> rules, RunContext ctx,
            Action? onGradeChanged = null)
        {
            CellNumber = cell.CellNumber;
            _antigens = cell.Antigens;
            _rules = rules;
            _ctx = ctx;
            _onGradeChanged = onGradeChanged;
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
            OnPropertyChanged(nameof(IsCcInvalid));
            OnPropertyChanged(nameof(HasEnteredGrade));
            OnPropertyChanged(nameof(IsIncomplete));
            _onGradeChanged?.Invoke();
        }

        private bool CanRuleOut(string antigen)
        {
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;
            if (!_antigens.ContainsKey(antithetical))
                return false;
            var antitheticalVal = _antigens.TryGetValue(antithetical, out var av) ? av : "-";
            if (antitheticalVal == "-") return true;
            return ReactionsViewModel.RuleAllowsHeterozygous(antigen, _rules);
        }

        private static bool IsNtOrZero(string v) => v == "NT" || v == "0" || string.IsNullOrEmpty(v);
    }

    public class CompareReactionRow
    {
        public string CellNumber { get; set; } = string.Empty;
        public string LeftLabel { get; set; } = string.Empty;
        public string RightLabel { get; set; } = string.Empty;
        public string LeftIS { get; set; } = "NT";
        public string LeftC37 { get; set; } = "NT";
        public string LeftAHG { get; set; } = "NT";
        public string LeftCC { get; set; } = "NT";
        public string RightIS { get; set; } = "NT";
        public string RightC37 { get; set; } = "NT";
        public string RightAHG { get; set; } = "NT";
        public string RightCC { get; set; } = "NT";

        public bool AhgChanged => LeftAHG != RightAHG;
        public bool AnyChanged =>
            LeftIS != RightIS || LeftC37 != RightC37 || LeftAHG != RightAHG || LeftCC != RightCC;
    }
}
