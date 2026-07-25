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

        public AnalysisViewModel(DatabaseService db, AntibodyAnalyzer analyzer, MainViewModel main)
        {
            _db = db;
            _analyzer = analyzer;
            _main = main;

            AnalyzeCommand = new RelayCommand(RunAnalysis, () => SelectedSpecimen != null);
            RefreshSpecimensCommand = new RelayCommand(Refresh);
            Refresh();
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
            if (SelectedSpecimen == null) return;

            IsStale = _db.IsSpecimenAnalysisStale(SelectedSpecimen.AccessionNumber);

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
            SummaryText = string.Empty;
        }

        private void PopulateFromResult(AnalysisResult result)
        {
            ClearResults();

            foreach (var (ab, prob) in result.Suspected.OrderByDescending(x => x.Value))
            {
                result.SuspectedStatistics.TryGetValue(ab, out var stats);
                SuspectedRows.Add(new SuspectedRow
                {
                    Antibody = ab,
                    Score = $"{prob * 100:F1}%",
                    FisherPValue = stats != null ? $"{stats.FisherPValue:F4}" : "-",
                    PatternScore = stats != null ? $"{stats.PatternScore:F3}" : "-",
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

            BuildSummary(result);
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
                    sb.AppendLine($"  {ab}  ({prob * 100:F1}%)");
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
}
