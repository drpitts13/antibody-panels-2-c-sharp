using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Compares an untreated serum run with an adsorbed aliquot on the same panel.
    /// Lost or remaining reactivity is reviewable evidence, not a diagnosis.
    /// </summary>
    public static class AbsorptionPatternClassifier
    {
        public const string RemovedKind = "AbsorptionRemoved";
        public const string SurvivedKind = "AbsorptionSurvived";
        public const string MixedKind = "AbsorptionMixed";
        public const string AutoRemovedKind = "AutoadsorptionRemoved";
        public const string AutoResidualKind = "AutoadsorptionResidual";
        public const string AutoSurvivedKind = "AutoadsorptionSurvived";
        public const string ResidualKind = "AbsorptionResidual";

        public static void Apply(
            AnalysisResult result,
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyDictionary<int, List<PanelCell>> cellsByPanel)
        {
            foreach (var adsCtx in contexts.Values.OrderBy(c => c.Run.RunId))
            {
                if (!IsAdsorption(adsCtx.Run.SerumTreatment)) continue;
                if (!byRun.TryGetValue(adsCtx.Run.RunId, out var adsorbedRxns)) continue;

                var control = contexts.Values
                    .Where(c => c.Run.SerumTreatment == SerumTreatment.None
                                && c.Run.PanelId == adsCtx.Run.PanelId
                                && c.Run.CellTreatment == adsCtx.Run.CellTreatment
                                && c.Run.RunId != adsCtx.Run.RunId)
                    .OrderBy(c => c.Run.RunId)
                    .FirstOrDefault();
                if (control == null) continue;
                if (!byRun.TryGetValue(control.Run.RunId, out var controlRxns)) continue;
                if (!cellsByPanel.TryGetValue(adsCtx.Run.PanelId, out var cells)) continue;

                var cellDict = cells.ToDictionary(c => c.CellNumber, StringComparer.OrdinalIgnoreCase);
                var controlByCell = controlRxns.ToDictionary(r => r.CellNumber, StringComparer.OrdinalIgnoreCase);
                var adsorbedByCell = adsorbedRxns.ToDictionary(r => r.CellNumber, StringComparer.OrdinalIgnoreCase);

                if (adsCtx.IsAutoAdsorbed)
                    AddAutoConclusion(result, adsCtx, control, cellDict, controlByCell, adsorbedByCell);
                else
                    AddAlloConclusions(result, adsCtx, control, cellDict, controlByCell, adsorbedByCell);
            }
        }

        private static void AddAlloConclusions(
            AnalysisResult result,
            RunContext adsCtx,
            RunContext control,
            Dictionary<string, PanelCell> cellDict,
            Dictionary<string, Reaction> controlByCell,
            Dictionary<string, Reaction> adsorbedByCell)
        {
            var expected = adsCtx.AbsorbedAntibodies;
            if (expected.Count == 0) return;
            var expectedText = string.Join(", ", expected);
            var label = adsCtx.Run.DisplayLabel;

            foreach (var antibody in expected)
            {
                var antigen = AntigenOf(antibody);
                var lost = new List<string>();
                var persisted = new List<string>();

                foreach (var (cellNum, adsorbedRxn) in adsorbedByCell)
                {
                    if (IsAutocontrol(cellNum)) continue;
                    if (!controlByCell.TryGetValue(cellNum, out var controlRxn)) continue;
                    if (!cellDict.TryGetValue(cellNum, out var cell)) continue;
                    if (!cell.IsAntigenPositive(antigen)) continue;
                    if (!control.IsPositive(controlRxn)) continue;

                    var display = CellLabel(cell, antigen);
                    if (adsCtx.IsPositive(adsorbedRxn))
                        persisted.Add(display);
                    else if (adsCtx.IsNegative(adsorbedRxn))
                        lost.Add(display);
                }

                var evaluated = lost.Count + persisted.Count;
                if (evaluated < 2) continue;

                if (lost.Count >= 2 && persisted.Count == 0)
                {
                    Add(result, new AbsorptionConclusion
                    {
                        AbsorptionLabel = label,
                        Antibody = antibody,
                        Kind = RemovedKind,
                        SupportingCells = lost,
                        AbsorbedOut = new List<string> { antibody },
                        EvidenceScore = Score(lost.Count, evaluated),
                        Explanation = WithScore(
                            $"{antibody} reactivity was lost on {lost.Count} antigen-positive cell(s) " +
                            $"({Join(lost)}) after {label}. Those cells support removal by adsorbing cells " +
                            $"expected to remove {expectedText}. No antigen-positive cell remained reactive. " +
                            "Not a diagnosis.",
                            Score(lost.Count, evaluated))
                    });
                }
                else if (persisted.Count >= 2 && lost.Count == 0)
                {
                    Add(result, new AbsorptionConclusion
                    {
                        AbsorptionLabel = label,
                        Antibody = antibody,
                        Kind = SurvivedKind,
                        SupportingCells = persisted,
                        Surviving = new List<string> { antibody },
                        EvidenceScore = Score(persisted.Count, evaluated),
                        Explanation = WithScore(
                            $"{antibody} remained reactive on {persisted.Count} antigen-positive cell(s) " +
                            $"({Join(persisted)}) after {label}. Survival argues against complete removal of " +
                            $"{antibody}. Adsorbing cells are expected to remove {expectedText}. Not a diagnosis.",
                            Score(persisted.Count, evaluated))
                    });
                }
                else if (lost.Count >= 1 && persisted.Count >= 1)
                {
                    Add(result, new AbsorptionConclusion
                    {
                        AbsorptionLabel = label,
                        Antibody = antibody,
                        Kind = MixedKind,
                        SupportingCells = lost,
                        ConflictingCells = persisted,
                        Surviving = new List<string> { antibody },
                        EvidenceScore = Score(lost.Count, evaluated),
                        Explanation = WithScore(
                            $"{antibody} reactivity was lost on cell(s) {Join(lost)} but remained reactive on " +
                            $"cell(s) {Join(persisted)} after {label}. Conflicting absorption evidence: do not " +
                            $"treat {antibody} as removed. Adsorbing cells are expected to remove {expectedText}. " +
                            "Not a diagnosis.",
                            Score(lost.Count, evaluated))
                    });
                }
            }

            var residual = new List<string>();
            foreach (var (cellNum, adsorbedRxn) in adsorbedByCell)
            {
                if (IsAutocontrol(cellNum)) continue;
                if (!controlByCell.TryGetValue(cellNum, out var controlRxn)) continue;
                if (!cellDict.TryGetValue(cellNum, out var cell)) continue;
                if (!control.IsPositive(controlRxn) || !adsCtx.IsPositive(adsorbedRxn)) continue;
                if (!LacksAllExpected(cell, expected)) continue;
                residual.Add(cell.CellNumber);
            }

            if (residual.Count == 0) return;
            Add(result, new AbsorptionConclusion
            {
                AbsorptionLabel = label,
                Kind = ResidualKind,
                SupportingCells = residual,
                Surviving = new List<string> { "residual reactivity" },
                EvidenceScore = 1,
                Explanation = WithScore(
                    $"Cell(s) {Join(residual)} remained reactive after {label} and are typed negative for " +
                    $"antigens whose antibodies that adsorption is expected to remove ({expectedText}). " +
                    "That residual reactivity is not explained by those specificities. Not a diagnosis.",
                    1)
            });
        }

        private static void AddAutoConclusion(
            AnalysisResult result,
            RunContext adsCtx,
            RunContext control,
            Dictionary<string, PanelCell> cellDict,
            Dictionary<string, Reaction> controlByCell,
            Dictionary<string, Reaction> adsorbedByCell)
        {
            var lost = new List<string>();
            var persisted = new List<string>();
            foreach (var (cellNum, adsorbedRxn) in adsorbedByCell)
            {
                if (IsAutocontrol(cellNum)) continue;
                if (!controlByCell.TryGetValue(cellNum, out var controlRxn)) continue;
                if (!cellDict.ContainsKey(cellNum)) continue;
                if (!control.IsPositive(controlRxn)) continue;

                if (adsCtx.IsPositive(adsorbedRxn))
                    persisted.Add(cellNum);
                else if (adsCtx.IsNegative(adsorbedRxn))
                    lost.Add(cellNum);
            }

            var paired = lost.Count + persisted.Count;
            if (paired < 3) return;
            var label = adsCtx.Run.DisplayLabel;

            if (lost.Count >= 3 && persisted.Count == 0)
            {
                Add(result, new AbsorptionConclusion
                {
                    AbsorptionLabel = label,
                    Kind = AutoRemovedKind,
                    SupportingCells = lost,
                    AbsorbedOut = new List<string> { "panreactive reactivity" },
                    EvidenceScore = Score(lost.Count, paired),
                    Explanation = WithScore(
                        $"{lost.Count} of {paired} paired cells lost reactivity after {label} ({Join(lost)}). " +
                        "This is consistent with removal of panreactive autoantibody reactivity. Not a diagnosis.",
                        Score(lost.Count, paired))
                });
            }
            else if (lost.Count >= 2 && persisted.Count >= 1)
            {
                Add(result, new AbsorptionConclusion
                {
                    AbsorptionLabel = label,
                    Kind = AutoResidualKind,
                    SupportingCells = lost,
                    ConflictingCells = persisted,
                    Surviving = new List<string> { "residual reactivity" },
                    EvidenceScore = Score(lost.Count, paired),
                    Explanation = WithScore(
                        $"{lost.Count} of {paired} paired cells lost reactivity after {label} ({Join(lost)}) but " +
                        $"{persisted.Count} remained reactive ({Join(persisted)}). Residual reactivity may be an " +
                        "underlying alloantibody or incomplete adsorption. Not a diagnosis.",
                        Score(lost.Count, paired))
                });
            }
            else if (persisted.Count >= 3 && lost.Count == 0)
            {
                Add(result, new AbsorptionConclusion
                {
                    AbsorptionLabel = label,
                    Kind = AutoSurvivedKind,
                    SupportingCells = persisted,
                    Surviving = new List<string> { "reactivity remained" },
                    EvidenceScore = Score(persisted.Count, paired),
                    Explanation = WithScore(
                        $"{persisted.Count} of {paired} paired cells remained reactive after {label} " +
                        $"({Join(persisted)}). Survival argues against a readily adsorbed autoantibody as the " +
                        "sole explanation. Not a diagnosis.",
                        Score(persisted.Count, paired))
                });
            }
        }

        private static bool LacksAllExpected(PanelCell cell, IReadOnlyList<string> antibodies)
        {
            if (antibodies.Count == 0) return false;
            foreach (var antibody in antibodies)
            {
                var antigen = AntigenOf(antibody);
                if (!cell.HasTypedAntigen(antigen)) return false;
                if (cell.IsAntigenPositive(antigen)) return false;
            }
            return true;
        }

        private static void Add(AnalysisResult result, AbsorptionConclusion conclusion) =>
            result.AbsorptionConclusions.Add(conclusion);

        private static bool IsAdsorption(SerumTreatment serum) =>
            serum is SerumTreatment.AlloAdsorptionR1R1
                or SerumTreatment.AlloAdsorptionR2R2
                or SerumTreatment.AlloAdsorptionRr
                or SerumTreatment.AutoAdsorption;

        private static bool IsAutocontrol(string cellNumber) =>
            string.Equals(cellNumber, "AC", StringComparison.OrdinalIgnoreCase);

        private static string AntigenOf(string antibody) =>
            antibody.StartsWith("anti-", StringComparison.OrdinalIgnoreCase)
                ? antibody[5..]
                : antibody;

        private static string CellLabel(PanelCell cell, string antigen) =>
            cell.IsHomozygousFor(antigen)
                ? $"{cell.CellNumber} (homozygous)"
                : cell.CellNumber;

        private static string Join(IReadOnlyList<string> cells) => string.Join(", ", cells);

        private static double Score(int supporting, int evaluated) =>
            evaluated <= 0 ? 0 : Math.Round(Math.Clamp(supporting / (double)evaluated, 0, 1), 2);

        private static string WithScore(string explanation, double score)
        {
            var text = explanation.TrimEnd();
            if (!text.Contains("evidence score", StringComparison.OrdinalIgnoreCase))
                text += $" Analytical evidence score {score:0.00}.";
            return text;
        }
    }
}
