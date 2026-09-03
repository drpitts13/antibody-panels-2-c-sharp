using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class AnalysisViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly AntibodyAnalyzer _analyzer;
        private readonly MainViewModel _main;

        public ObservableCollection<Specimen> Specimens { get; } = new();
        public ObservableCollection<SuspectedRow> SuspectedRows { get; } = new();
        public ObservableCollection<RuleoutRow> RuleoutRows { get; } = new();
        public ObservableCollection<PatternRow> PatternRows { get; } = new();
        public ObservableCollection<CombinationRow> CombinationRows { get; } = new();
        public ObservableCollection<GatedRuleoutRow> GatedRuleoutRows { get; } = new();
        public ObservableCollection<TreatmentInferenceRow> TreatmentInferenceRows { get; } = new();
        public ObservableCollection<AbsorptionConclusionRow> AbsorptionConclusionRows { get; } = new();
        public ObservableCollection<EvidenceCellRow> SupportingCells { get; } = new();
        public ObservableCollection<EvidenceCellRow> ConflictingCells { get; } = new();
        public ObservableCollection<RuleoutDetailRow> RuleoutDetailRows { get; } = new();
        public ObservableCollection<DosageRow> DosageRows { get; } = new();
        public ObservableCollection<string> SuggestionItems { get; } = new();

        private AnalysisResult? _lastResult;

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set { if (SetField(ref _selectedSpecimen, value)) AutoLoadAnalysis(); }
        }

        private string _summaryText = string.Empty;
        public string SummaryText
        {
            get => _summaryText;
            set => SetField(ref _summaryText, value);
        }

        private bool _isStale;
        public bool IsStale
        {
            get => _isStale;
            set => SetField(ref _isStale, value);
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand RefreshSpecimensCommand { get; }
        public ICommand ConfirmIdCommand { get; }
        public ICommand ClearConfirmationCommand { get; }
        public ICommand AddSelectedToFinalIdCommand { get; }

        private string _finalAntibodiesText = string.Empty;
        public string FinalAntibodiesText
        {
            get => _finalAntibodiesText;
            set => SetField(ref _finalAntibodiesText, value);
        }

        private string _finalComment = string.Empty;
        public string FinalComment
        {
            get => _finalComment;
            set => SetField(ref _finalComment, value);
        }

        private string _identifiedBy = string.Empty;
        public string IdentifiedBy
        {
            get => _identifiedBy;
            set => SetField(ref _identifiedBy, value);
        }

        private string _finalCallStatus = "Final identification: not confirmed.";
        public string FinalCallStatus
        {
            get => _finalCallStatus;
            set => SetField(ref _finalCallStatus, value);
        }

        private SuspectedRow? _selectedSuspected;
        public SuspectedRow? SelectedSuspected
        {
            get => _selectedSuspected;
            set
            {
                if (SetField(ref _selectedSuspected, value))
                {
                    LoadSuspectedEvidence();
                    if (AddSelectedToFinalIdCommand is RelayCommand addCmd)
                        addCmd.RaiseCanExecuteChanged();
                }
            }
        }

        private RuleoutRow? _selectedRuleout;
        public RuleoutRow? SelectedRuleout
        {
            get => _selectedRuleout;
            set
            {
                if (SetField(ref _selectedRuleout, value))
                    LoadRuleoutDetails();
            }
        }

        public bool HasSuggestions => SuggestionItems.Count > 0;

        public AnalysisViewModel(DatabaseService db, AntibodyAnalyzer analyzer, MainViewModel main)
        {
            _db = db;
            _analyzer = analyzer;
            _main = main;

            AnalyzeCommand = new RelayCommand(RunAnalysis, () => SelectedSpecimen != null);
            RefreshSpecimensCommand = new RelayCommand(Refresh);
            ConfirmIdCommand = new RelayCommand(ConfirmId, () => SelectedSpecimen != null);
            ClearConfirmationCommand = new RelayCommand(ClearConfirmation, () => SelectedSpecimen != null);
            AddSelectedToFinalIdCommand = new RelayCommand(AddSelectedToFinalId,
                () => SelectedSpecimen != null && SelectedSuspected != null);
            Refresh();
        }

        public void SelectSpecimen(string accessionNumber)
        {
            SelectedSpecimen = Specimens.FirstOrDefault(s => s.AccessionNumber == accessionNumber)
                ?? SelectedSpecimen;
        }

        public void Refresh()
        {
            var sid = SelectedSpecimen?.AccessionNumber;
            Specimens.Clear();
            foreach (var s in _db.GetAllSpecimens()) Specimens.Add(s);
            SelectedSpecimen = sid != null
                ? Specimens.FirstOrDefault(s => s.AccessionNumber == sid)
                : Specimens.FirstOrDefault();
        }

        public void OnTabSelected()
        {
            if (SelectedSpecimen != null)
                IsStale = _db.IsSpecimenAnalysisStale(SelectedSpecimen.AccessionNumber);
        }

        /// <summary>
        /// Called automatically when the selected specimen changes.
        /// If the specimen has been analyzed before, reloads those results without
        /// updating the database (preserving the stale-analysis indicator).
        /// </summary>
        private void AutoLoadAnalysis()
        {
            ClearResults();
            if (SelectedSpecimen == null)
            {
                LoadFinalCall(null);
                return;
            }

            IsStale = _db.IsSpecimenAnalysisStale(SelectedSpecimen.AccessionNumber);
            LoadFinalCall(SelectedSpecimen);

            // Only auto-populate if a previous analysis run exists
            if (SelectedSpecimen.LastAnalyzedAt == null) return;

            try
            {
                var result = _analyzer.AnalyzeSpecimen(SelectedSpecimen.AccessionNumber, updateDb: false);
                PopulateFromResult(result);
            }
            catch { /* silently ignore — user can click Run Analysis to retry */ }
        }

        /// <summary>Triggered by the Run Analysis button — computes, persists, and refreshes.</summary>
        private void RunAnalysis()
        {
            if (SelectedSpecimen == null) return;
            _main.SetStatus("Running analysis...");

            var result = _analyzer.AnalyzeSpecimen(SelectedSpecimen.AccessionNumber, updateDb: true);
            PopulateFromResult(result);
            IsStale = false;
            _main.SetStatus($"Analysis complete — {SuspectedRows.Count} suspected, {RuleoutRows.Count} ruled out.");
            _main.SpecimensVM.Refresh();
            _main.WorklistVM.Refresh();
        }

        private void ClearResults()
        {
            SuspectedRows.Clear();
            RuleoutRows.Clear();
            PatternRows.Clear();
            CombinationRows.Clear();
            GatedRuleoutRows.Clear();
            TreatmentInferenceRows.Clear();
            AbsorptionConclusionRows.Clear();
            SupportingCells.Clear();
            ConflictingCells.Clear();
            RuleoutDetailRows.Clear();
            DosageRows.Clear();
            SuggestionItems.Clear();
            OnPropertyChanged(nameof(HasSuggestions));
            SummaryText = string.Empty;
            _lastResult = null;
            _selectedSuspected = null;
            _selectedRuleout = null;
            OnPropertyChanged(nameof(SelectedSuspected));
            OnPropertyChanged(nameof(SelectedRuleout));
        }

        private void PopulateFromResult(AnalysisResult result)
        {
            ClearResults();
            _lastResult = result;

            foreach (var (ab, prob) in result.Suspected.OrderByDescending(x => x.Value))
            {
                result.SuspectedStatistics.TryGetValue(ab, out var stats);
                SuspectedRows.Add(new SuspectedRow
                {
                    Antibody = ab,
                    Score = $"{prob * 100:F1}%",
                    FisherPValue = stats != null ? $"{stats.FisherPValue:F4}" : "-",
                    PatternScore = stats != null ? $"{stats.PatternScore:F3}" : "-",
                    AgPositiveReactive = stats != null
                        ? $"{stats.PositiveAgPositiveCount} / {stats.IdentificationRequired}" : "-",
                    AgNegativeNonreactive = stats != null
                        ? $"{stats.NegativeAgNegativeCount} / {stats.IdentificationRequired}" : "-",
                    IdentificationRule = stats?.IdentificationStatus ?? "-",
                    MeetsIdentificationRule = stats?.MeetsIdentificationRule ?? false,
                });
            }

            foreach (var (ab, cnt) in result.RuledOut.OrderBy(x => x.Key))
                RuleoutRows.Add(new RuleoutRow { Antibody = ab, Count = cnt });

            foreach (var pm in result.PatternMatches.Take(20))
                PatternRows.Add(new PatternRow
                {
                    Antibody = pm.Antibody,
                    Matches = pm.Matches,
                    Mismatches = pm.Mismatches,
                    Confidence = $"{pm.Confidence * 100:F1}%"
                });

            foreach (var c in result.Combinations)
            {
                CombinationRows.Add(new CombinationRow
                {
                    Antibodies = string.Join(" + ", c.Antibodies),
                    IndividualScores = string.Join(" / ", c.Probabilities.Select(p => $"{p * 100:F1}%")),
                    CombinationScore = $"{c.CombinationScore * 100:F1}%",
                    BothSupport = c.BothSupport,
                    Ab1Only = c.Ab1Only,
                    Ab2Only = c.Ab2Only,
                    Neither = c.Neither
                });
            }

            foreach (var g in result.GatedRuleouts)
                GatedRuleoutRows.Add(new GatedRuleoutRow
                {
                    Antibody = g.Antibody,
                    TreatmentLabel = g.CellTreatmentLabel,
                    Reason = g.Reason,
                });

            foreach (var inf in result.TreatmentInferences)
                TreatmentInferenceRows.Add(new TreatmentInferenceRow
                {
                    RunLabel = inf.RunLabel,
                    Antibody = inf.Antibody,
                    Observation = inf.Observation,
                });

            foreach (var abs in result.AbsorptionConclusions)
                AbsorptionConclusionRows.Add(new AbsorptionConclusionRow
                {
                    AbsorptionLabel = abs.AbsorptionLabel,
                    AbsorbedOut = string.Join(", ", abs.AbsorbedOut),
                    Surviving = string.Join(", ", abs.Surviving),
                });

            foreach (var de in result.DosageEffects)
                DosageRows.Add(new DosageRow
                {
                    Antibody = de.Antibody,
                    Antigen = de.Antigen,
                    AvgHomozygous = de.AvgHomozygous.ToString("F2"),
                    AvgHeterozygous = de.AvgHeterozygous.ToString("F2"),
                    HomozygousCount = de.HomozygousCount,
                    HeterozygousCount = de.HeterozygousCount,
                    Severity = de.Severity,
                });

            foreach (var s in result.Suggestions)
                SuggestionItems.Add(s);
            OnPropertyChanged(nameof(HasSuggestions));

            SelectedSuspected = SuspectedRows.FirstOrDefault();
            SelectedRuleout = RuleoutRows.FirstOrDefault();

            BuildSummary(result);
            LoadFinalCall(SelectedSpecimen);
        }

        private void LoadFinalCall(Specimen? specimen)
        {
            if (specimen == null)
            {
                FinalAntibodiesText = string.Empty;
                FinalComment = string.Empty;
                IdentifiedBy = string.Empty;
                FinalCallStatus = "Final identification: not confirmed.";
                return;
            }

            var fresh = _db.GetSpecimen(specimen.AccessionNumber) ?? specimen;
            if (fresh.HasFinalCall)
            {
                FinalAntibodiesText = fresh.FinalAntibodies ?? "";
                FinalComment = fresh.FinalComment ?? "";
                IdentifiedBy = fresh.IdentifiedBy ?? "";
                FinalCallStatus = $"Final identification: confirmed by {fresh.IdentifiedBy} on {fresh.IdentifiedAt}.";
            }
            else
            {
                FinalAntibodiesText = SuggestedFinalId(SuspectedRows);
                FinalComment = "";
                FinalCallStatus = SuspectedRows.Any(r => r.MeetsIdentificationRule)
                    ? "Final identification: not confirmed. Green rows meet the ID rule — add or edit, then confirm."
                    : "Final identification: not confirmed. No antibody yet meets the ID rule. Double-click a row to add it.";
            }
        }

        public static string SuggestedFinalId(IEnumerable<SuspectedRow> rows) =>
            string.Join("; ", rows.Where(r => r.MeetsIdentificationRule).Select(r => r.Antibody));

        public static string AppendAntibodyToFinalId(string? current, string antibody)
        {
            if (string.IsNullOrWhiteSpace(antibody))
                return current?.Trim() ?? string.Empty;

            var parts = (current ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();
            if (parts.Any(p => string.Equals(p, antibody, StringComparison.OrdinalIgnoreCase)))
                return string.Join("; ", parts);
            parts.Add(antibody.Trim());
            return string.Join("; ", parts);
        }

        private void AddSelectedToFinalId()
        {
            if (SelectedSuspected == null) return;
            FinalAntibodiesText = AppendAntibodyToFinalId(FinalAntibodiesText, SelectedSuspected.Antibody);
            _main.SetStatus($"Added {SelectedSuspected.Antibody} to Final ID.");
        }

        private void ConfirmId()
        {
            if (SelectedSpecimen == null) return;
            var antibodies = FinalAntibodiesText.Trim();
            var initials = IdentifiedBy.Trim();
            if (string.IsNullOrWhiteSpace(antibodies))
            {
                MessageBox.Show("Enter the confirmed antibody identification.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(initials))
            {
                MessageBox.Show("Initials are required to confirm identification.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _db.SetSpecimenFinalCall(SelectedSpecimen.AccessionNumber, antibodies,
                string.IsNullOrWhiteSpace(FinalComment) ? null : FinalComment.Trim(), initials);
            LoadFinalCall(SelectedSpecimen);
            _main.SpecimensVM.Refresh();
            _main.ReportsVM.Refresh();
            _main.WorklistVM.Refresh();
            _main.SetStatus($"Identification confirmed for {SelectedSpecimen.AccessionNumber}.");
        }

        private void ClearConfirmation()
        {
            if (SelectedSpecimen == null) return;
            if (MessageBox.Show("Clear the confirmed identification for this specimen?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _db.ClearSpecimenFinalCall(SelectedSpecimen.AccessionNumber);
            IdentifiedBy = string.Empty;
            FinalComment = string.Empty;
            LoadFinalCall(SelectedSpecimen);
            _main.SpecimensVM.Refresh();
            _main.ReportsVM.Refresh();
            _main.WorklistVM.Refresh();
            _main.SetStatus("Confirmed identification cleared.");
        }

        private void LoadSuspectedEvidence()
        {
            SupportingCells.Clear();
            ConflictingCells.Clear();
            if (_lastResult == null || SelectedSuspected == null) return;
            if (!_lastResult.SuspectedEvidence.TryGetValue(SelectedSuspected.Antibody, out var ev))
                return;
            foreach (var c in ev.SupportingCells)
                SupportingCells.Add(EvidenceCellRow.From(c, "Supporting"));
            foreach (var c in ev.ConflictingCells)
                ConflictingCells.Add(EvidenceCellRow.From(c, "Conflicting"));
        }

        private void LoadRuleoutDetails()
        {
            RuleoutDetailRows.Clear();
            if (_lastResult == null || SelectedRuleout == null) return;
            if (!_lastResult.DetailedRuleouts.TryGetValue(SelectedRuleout.Antibody, out var details))
                return;
            foreach (var d in details)
            {
                RuleoutDetailRows.Add(new RuleoutDetailRow
                {
                    PanelName = d.PanelName,
                    RunLabel = d.RunLabel,
                    CellNumber = d.CellNumber,
                    Zygosity = d.IsHomozygous ? "Homozygous" : "Heterozygous",
                    IS = d.IS,
                    C37 = d.C37,
                    AHG = d.AHG,
                    CC = d.CC,
                });
            }
        }

        private void BuildSummary(AnalysisResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Analysis Results: {r.SpecimenId} ===");
            sb.AppendLine();

            if (r.Suspected.Count > 0)
            {
                sb.AppendLine("SUSPECTED ANTIBODIES:");
                foreach (var (ab, prob) in r.Suspected.OrderByDescending(x => x.Value))
                {
                    r.SuspectedStatistics.TryGetValue(ab, out var stats);
                    var id = stats != null ? $"  {stats.IdentificationDetail}" : "";
                    sb.AppendLine($"  {ab}  ({prob * 100:F1}%){id}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("No antibodies suspected based on current data.");
                sb.AppendLine();
            }

            if (r.RuledOut.Count > 0)
            {
                sb.AppendLine($"RULED OUT ({r.RuledOut.Count} antibodies):");
                foreach (var (ab, cnt) in r.RuledOut.OrderBy(x => x.Key))
                    sb.AppendLine($"  {ab}  (x{cnt})");
                sb.AppendLine();
            }

            if (r.DosageEffects.Count > 0)
            {
                sb.AppendLine("DOSAGE EFFECTS DETECTED:");
                foreach (var de in r.DosageEffects)
                    sb.AppendLine($"  {de.Antibody}: homo avg {de.AvgHomozygous:F2}, het avg {de.AvgHeterozygous:F2} [{de.Severity}]");
                sb.AppendLine();
            }

            if (r.GatedRuleouts.Count > 0)
            {
                sb.AppendLine($"GATED RULE-OUTS ({r.GatedRuleouts.Count}):");
                foreach (var g in r.GatedRuleouts)
                    sb.AppendLine($"  ⚠ {g.Antibody} — {g.Reason}");
                sb.AppendLine();
            }

            if (r.TreatmentInferences.Count > 0)
            {
                sb.AppendLine("TREATMENT INFERENCES:");
                foreach (var inf in r.TreatmentInferences)
                    sb.AppendLine($"  • {inf.Observation}");
                sb.AppendLine();
            }

            if (r.AbsorptionConclusions.Count > 0)
            {
                sb.AppendLine("ABSORPTION CONCLUSIONS:");
                foreach (var abs in r.AbsorptionConclusions)
                {
                    sb.AppendLine($"  {abs.AbsorptionLabel}:");
                    if (abs.AbsorbedOut.Count > 0)
                        sb.AppendLine($"    Absorbed out: {string.Join(", ", abs.AbsorbedOut)}");
                    if (abs.Surviving.Count > 0)
                        sb.AppendLine($"    Surviving: {string.Join(", ", abs.Surviving)}");
                }
                sb.AppendLine();
            }

            if (r.Suggestions.Count > 0)
            {
                sb.AppendLine("SUGGESTIONS:");
                foreach (var s in r.Suggestions)
                    sb.AppendLine($"  • {s}");
            }

            SummaryText = sb.ToString();
        }
    }

    public class SuspectedRow
    {
        public string Antibody { get; set; } = string.Empty;
        public string Score { get; set; } = string.Empty;
        public string FisherPValue { get; set; } = string.Empty;
        public string PatternScore { get; set; } = string.Empty;
        public string AgPositiveReactive { get; set; } = string.Empty;
        public string AgNegativeNonreactive { get; set; } = string.Empty;
        public string IdentificationRule { get; set; } = string.Empty;
        public bool MeetsIdentificationRule { get; set; }
    }

    public class RuleoutRow
    {
        public string Antibody { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PatternRow
    {
        public string Antibody { get; set; } = string.Empty;
        public int Matches { get; set; }
        public int Mismatches { get; set; }
        public string Confidence { get; set; } = string.Empty;
    }

    public class CombinationRow
    {
        public string Antibodies { get; set; } = string.Empty;
        public string IndividualScores { get; set; } = string.Empty;
        public string CombinationScore { get; set; } = string.Empty;
        public int BothSupport { get; set; }
        public int Ab1Only { get; set; }
        public int Ab2Only { get; set; }
        public int Neither { get; set; }
    }

    public class GatedRuleoutRow
    {
        public string Antibody { get; set; } = string.Empty;
        public string TreatmentLabel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class TreatmentInferenceRow
    {
        public string RunLabel { get; set; } = string.Empty;
        public string Antibody { get; set; } = string.Empty;
        public string Observation { get; set; } = string.Empty;
    }

    public class AbsorptionConclusionRow
    {
        public string AbsorptionLabel { get; set; } = string.Empty;
        public string AbsorbedOut { get; set; } = string.Empty;
        public string Surviving { get; set; } = string.Empty;
    }

    public class EvidenceCellRow
    {
        public string Kind { get; set; } = string.Empty;
        public string PanelName { get; set; } = string.Empty;
        public string RunLabel { get; set; } = string.Empty;
        public string CellNumber { get; set; } = string.Empty;
        public string IS { get; set; } = string.Empty;
        public string C37 { get; set; } = string.Empty;
        public string AHG { get; set; } = string.Empty;
        public string CC { get; set; } = string.Empty;
        public string Strongest { get; set; } = string.Empty;

        public static EvidenceCellRow From(EvidenceCell c, string kind) => new()
        {
            Kind = kind,
            PanelName = c.PanelName,
            RunLabel = c.RunLabel,
            CellNumber = c.CellNumber,
            IS = c.IS,
            C37 = c.C37,
            AHG = c.AHG,
            CC = c.CC,
            Strongest = $"{c.StrongestPhase} {c.StrongestValue}".Trim(),
        };
    }

    public class RuleoutDetailRow
    {
        public string PanelName { get; set; } = string.Empty;
        public string RunLabel { get; set; } = string.Empty;
        public string CellNumber { get; set; } = string.Empty;
        public string Zygosity { get; set; } = string.Empty;
        public string IS { get; set; } = string.Empty;
        public string C37 { get; set; } = string.Empty;
        public string AHG { get; set; } = string.Empty;
        public string CC { get; set; } = string.Empty;
    }

    public class DosageRow
    {
        public string Antibody { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public string AvgHomozygous { get; set; } = string.Empty;
        public string AvgHeterozygous { get; set; } = string.Empty;
        public int HomozygousCount { get; set; }
        public int HeterozygousCount { get; set; }
        public string Severity { get; set; } = string.Empty;
    }
}
