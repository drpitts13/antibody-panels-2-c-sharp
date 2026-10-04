using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Compares untreated vs enzyme-treated (ficin/papain) runs.
    /// Lost, gained, or surviving reactivity is reviewable evidence, not a diagnosis.
    /// </summary>
    public static class EnzymeTreatmentPatternClassifier
    {
        public const string DestroyedKind = "EnzymeDestroyed";
        public const string EnhancedKind = "EnzymeEnhanced";
        public const string ResistantKind = "EnzymeResistant";

        public static void Apply(
            AnalysisResult result,
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyDictionary<int, List<PanelCell>> cellsByPanel)
        {
            var untreated = contexts.Values.Where(c => c.Run.IsUntreated).ToList();
            var enzyme = contexts.Values.Where(c => IsEnzyme(c.Run.CellTreatment)).ToList();
            if (untreated.Count == 0 || enzyme.Count == 0) return;

            foreach (var treatedCtx in enzyme)
            {
                if (!byRun.TryGetValue(treatedCtx.Run.RunId, out var treatedRxns)) continue;
                var untreatedCtx = untreated.FirstOrDefault(c => c.Run.PanelId == treatedCtx.Run.PanelId);
                if (untreatedCtx == null) continue;
                if (!byRun.TryGetValue(untreatedCtx.Run.RunId, out var untreatedRxns)) continue;
                if (!cellsByPanel.TryGetValue(treatedCtx.Run.PanelId, out var cells)) continue;

                var cellDict = cells.ToDictionary(c => c.CellNumber, StringComparer.OrdinalIgnoreCase);
                var treatedByCell = treatedRxns.ToDictionary(r => r.CellNumber, StringComparer.OrdinalIgnoreCase);
                var untreatedByCell = untreatedRxns.ToDictionary(r => r.CellNumber, StringComparer.OrdinalIgnoreCase);

                var lost = new List<string>();
                var gained = new List<string>();
                var persisted = new List<string>();
                var destroyedOnLost = new HashSet<string>(StringComparer.Ordinal);
                var enhancedOnGained = new HashSet<string>(StringComparer.Ordinal);
                var destroyedOnPersisted = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (cellNum, treatedRxn) in treatedByCell)
                {
                    if (string.Equals(cellNum, "AC", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!untreatedByCell.TryGetValue(cellNum, out var untreatedRxn)) continue;
                    if (!cellDict.TryGetValue(cellNum, out var cell)) continue;

                    var treatedPos = treatedCtx.IsPositive(treatedRxn);
                    var untreatedPos = untreatedCtx.IsPositive(untreatedRxn);
                    if (!untreatedPos && !treatedPos) continue;

                    var destroyed = TypedAntigensWithEffect(cell, treatedCtx.Run.CellTreatment, AntigenEffect.Destroyed);
                    var enhanced = TypedAntigensWithEffect(cell, treatedCtx.Run.CellTreatment, AntigenEffect.Enhanced);

                    if (untreatedPos && !treatedPos)
                    {
                        lost.Add(cellNum);
                        foreach (var ag in destroyed) destroyedOnLost.Add(ag);
                    }
                    else if (!untreatedPos && treatedPos)
                    {
                        gained.Add(cellNum);
                        foreach (var ag in enhanced) enhancedOnGained.Add(ag);
                    }
                    else
                    {
                        persisted.Add(cellNum);
                        foreach (var ag in destroyed) destroyedOnPersisted.Add(ag);
                    }
                }

                var paired = lost.Count + gained.Count + persisted.Count;
                if (paired < 3) continue;

                var treatment = AntigenTreatmentEffects.GetDisplayName(treatedCtx.Run.CellTreatment);

                if (lost.Count >= 2 && lost.Count >= gained.Count)
                {
                    var antigens = destroyedOnLost.Count > 0
                        ? string.Join(", ", destroyedOnLost.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "ficin-destroyed antigens (Fya/Fyb, M/N, S/s, Lewis, Xg)";
                    var note = MakeNote(
                        DestroyedKind, lost, persisted.Concat(gained).ToList(), paired,
                        $"{lost.Count} of {paired} paired cells lost reactivity on {treatment} cells " +
                        $"({string.Join(", ", lost)}). This is consistent with antibodies to enzyme-destroyed " +
                        $"antigens ({antigens}). Do not rule those antibodies out from the enzyme run. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                    AddInference(result, treatedCtx.Run.DisplayLabel, destroyedOnLost,
                        TreatmentInferenceType.ReactivityLostOnEnzyme, note.Explanation);
                }
                else if (lost.Count >= 1 && persisted.Count >= 2)
                {
                    var antigens = destroyedOnLost.Count > 0
                        ? string.Join(", ", destroyedOnLost.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "enzyme-destroyed antigens";
                    var note = MakeNote(
                        DestroyedKind, lost, persisted, paired,
                        $"{lost.Count} of {paired} paired cells lost reactivity on {treatment} " +
                        $"({string.Join(", ", lost)}) but {persisted.Count} remained reactive " +
                        $"({string.Join(", ", persisted)}). Enzyme-destroyed antigens ({antigens}) are only a partial " +
                        "fit; consider multiple antibodies or an enzyme-resistant specificity. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                }

                if (gained.Count >= 2 && gained.Count > lost.Count)
                {
                    var antigens = enhancedOnGained.Count > 0
                        ? string.Join(", ", enhancedOnGained.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "enzyme-enhanced antigens (Rh, Kidd, P1)";
                    var note = MakeNote(
                        EnhancedKind, gained, lost, paired,
                        $"{gained.Count} of {paired} paired cells became reactive on {treatment} cells " +
                        $"({string.Join(", ", gained)}). This is consistent with weak antibodies to enzyme-enhanced " +
                        $"antigens ({antigens}). Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                    AddInference(result, treatedCtx.Run.DisplayLabel, enhancedOnGained,
                        TreatmentInferenceType.ReactivityGainedOnEnzyme, note.Explanation);
                }
                else if (persisted.Count >= 3 && lost.Count == 0 && gained.Count == 0)
                {
                    var antigens = destroyedOnPersisted.Count > 0
                        ? string.Join(", ", destroyedOnPersisted.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "Fya, M, S";
                    var note = MakeNote(
                        ResistantKind, persisted, Array.Empty<string>(), paired,
                        $"{persisted.Count} of {paired} paired cells remained reactive on {treatment} cells " +
                        $"({string.Join(", ", persisted)}). Survival of reactivity argues against antibodies to " +
                        $"enzyme-destroyed antigens ({antigens}) as the sole explanation. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                }
            }
        }

        private static bool IsEnzyme(CellTreatment treatment) =>
            treatment is CellTreatment.Ficin or CellTreatment.Papain;

        private static IReadOnlyList<string> TypedAntigensWithEffect(
            PanelCell cell, CellTreatment treatment, AntigenEffect wanted)
        {
            var list = new List<string>();
            foreach (var ag in AntigenConstants.AllKnownAntigens)
            {
                if (!cell.IsAntigenPositive(ag)) continue;
                if (AntigenTreatmentEffects.GetCellEffect(treatment, ag) == wanted)
                    list.Add(ag);
            }
            return list;
        }

        private static ReactionPatternNote MakeNote(
            string kind, IReadOnlyList<string> supporting, IReadOnlyList<string> conflicting,
            int paired, string explanation)
        {
            var score = paired <= 0 ? 0 : Math.Round(Math.Clamp(supporting.Count / (double)paired, 0, 1), 2);
            var expl = explanation.TrimEnd();
            if (!expl.Contains("evidence score", StringComparison.OrdinalIgnoreCase))
                expl += $" Analytical evidence score {score:0.00}.";
            return new ReactionPatternNote
            {
                Kind = kind,
                MatchingCells = supporting.Count,
                EvaluatedCells = paired,
                SupportingCells = supporting.ToList(),
                ConflictingCells = conflicting.ToList(),
                EvidenceScore = score,
                Explanation = expl
            };
        }

        private static void AddInference(
            AnalysisResult result, string runLabel, IEnumerable<string> antigens,
            TreatmentInferenceType type, string observation)
        {
            foreach (var ag in antigens.Take(6))
            {
                var antibody = $"anti-{ag}";
                if (result.TreatmentInferences.Any(i =>
                        string.Equals(i.Antibody, antibody, StringComparison.OrdinalIgnoreCase) &&
                        i.InferenceType == type))
                    continue;
                result.TreatmentInferences.Add(new TreatmentInference
                {
                    RunLabel = runLabel,
                    Antibody = antibody,
                    InferenceType = type,
                    Observation = observation,
                });
            }
        }
    }
}
