using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Compares untreated vs DTT-treated runs on the same panel.
    /// Lost or surviving reactivity is reviewable evidence, not a diagnosis.
    /// </summary>
    public static class DttTreatmentPatternClassifier
    {
        public const string DestroyedKind = "DttDestroyed";
        public const string ResistantKind = "DttResistant";

        public static void Apply(
            AnalysisResult result,
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyDictionary<int, List<PanelCell>> cellsByPanel)
        {
            var untreated = contexts.Values.Where(c => c.Run.IsUntreated).ToList();
            var dtt = contexts.Values.Where(c => c.Run.CellTreatment == CellTreatment.DTT).ToList();
            if (untreated.Count == 0 || dtt.Count == 0) return;

            foreach (var treatedCtx in dtt)
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
                var persisted = new List<string>();
                var destroyedOnLost = new HashSet<string>(StringComparer.Ordinal);
                var destroyedOnPersisted = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (cellNum, treatedRxn) in treatedByCell)
                {
                    if (string.Equals(cellNum, "AC", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!untreatedByCell.TryGetValue(cellNum, out var untreatedRxn)) continue;
                    if (!cellDict.TryGetValue(cellNum, out var cell)) continue;

                    var treatedPos = treatedCtx.IsPositive(treatedRxn);
                    var untreatedPos = untreatedCtx.IsPositive(untreatedRxn);
                    if (!untreatedPos && !treatedPos) continue;

                    var destroyed = TypedAntigensWithEffect(cell, AntigenEffect.Destroyed);

                    if (untreatedPos && !treatedPos)
                    {
                        lost.Add(cellNum);
                        foreach (var ag in destroyed) destroyedOnLost.Add(ag);
                    }
                    else if (untreatedPos && treatedPos)
                    {
                        persisted.Add(cellNum);
                        foreach (var ag in destroyed) destroyedOnPersisted.Add(ag);
                    }
                }

                var paired = lost.Count + persisted.Count;
                // Count newly reactive DTT cells in the paired denominator when present,
                // but they do not form a DTT-enhanced pattern (DTT has no enhanced table).
                foreach (var (cellNum, treatedRxn) in treatedByCell)
                {
                    if (string.Equals(cellNum, "AC", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!untreatedByCell.TryGetValue(cellNum, out var untreatedRxn)) continue;
                    if (untreatedCtx.IsPositive(untreatedRxn)) continue;
                    if (treatedCtx.IsPositive(treatedRxn))
                        paired++;
                }

                if (paired < 3) continue;

                var treatment = AntigenTreatmentEffects.GetDisplayName(CellTreatment.DTT);

                if (lost.Count >= 2 && lost.Count >= persisted.Count)
                {
                    var antigens = destroyedOnLost.Count > 0
                        ? string.Join(", ", destroyedOnLost.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "DTT-destroyed antigens (K/k, Lua/Lub, Yt, Knops, Do)";
                    var note = MakeNote(
                        DestroyedKind, lost, persisted, paired,
                        $"{lost.Count} of {paired} paired cells lost reactivity on {treatment} cells " +
                        $"({string.Join(", ", lost)}). This is consistent with antibodies to DTT-destroyed " +
                        $"antigens ({antigens}). Do not rule those antibodies out from the DTT run. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                    AddInference(result, treatedCtx.Run.DisplayLabel, destroyedOnLost, note.Explanation);
                }
                else if (lost.Count >= 1 && persisted.Count >= 2)
                {
                    var antigens = destroyedOnLost.Count > 0
                        ? string.Join(", ", destroyedOnLost.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "DTT-destroyed antigens";
                    var note = MakeNote(
                        DestroyedKind, lost, persisted, paired,
                        $"{lost.Count} of {paired} paired cells lost reactivity on {treatment} " +
                        $"({string.Join(", ", lost)}) but {persisted.Count} remained reactive " +
                        $"({string.Join(", ", persisted)}). DTT-destroyed antigens ({antigens}) are only a partial " +
                        "fit; consider multiple antibodies or a DTT-resistant specificity. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                }
                else if (persisted.Count >= 3 && lost.Count == 0)
                {
                    var antigens = destroyedOnPersisted.Count > 0
                        ? string.Join(", ", destroyedOnPersisted.OrderBy(a => a, StringComparer.Ordinal).Take(8))
                        : "K, k, Lua, Lub";
                    var note = MakeNote(
                        ResistantKind, persisted, Array.Empty<string>(), paired,
                        $"{persisted.Count} of {paired} paired cells remained reactive on {treatment} cells " +
                        $"({string.Join(", ", persisted)}). Survival of reactivity argues against antibodies to " +
                        $"DTT-destroyed antigens ({antigens}) as the sole explanation. Not a diagnosis.");
                    result.ReactionPatterns.Add(note);
                }
            }
        }

        private static IReadOnlyList<string> TypedAntigensWithEffect(PanelCell cell, AntigenEffect wanted)
        {
            var list = new List<string>();
            foreach (var ag in AntigenConstants.AllKnownAntigens)
            {
                if (!cell.IsAntigenPositive(ag)) continue;
                if (AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, ag) == wanted)
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
            AnalysisResult result, string runLabel, IEnumerable<string> antigens, string observation)
        {
            foreach (var ag in antigens.Take(6))
            {
                var antibody = $"anti-{ag}";
                if (result.TreatmentInferences.Any(i =>
                        string.Equals(i.Antibody, antibody, StringComparison.OrdinalIgnoreCase) &&
                        i.InferenceType == TreatmentInferenceType.ReactivityLostOnDTT))
                    continue;
                result.TreatmentInferences.Add(new TreatmentInference
                {
                    RunLabel = runLabel,
                    Antibody = antibody,
                    InferenceType = TreatmentInferenceType.ReactivityLostOnDTT,
                    Observation = observation,
                });
            }
        }
    }
}
