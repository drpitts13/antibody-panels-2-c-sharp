using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public readonly record struct PatternCellObservation(
        string CellNumber,
        bool Evaluated,
        bool AnyReactive,
        bool ColdPhaseReactive,
        bool IatPhaseReactive);

    /// <summary>
    /// Labels cold, warm/IAT, mixed-phase, and panreactive patterns as
    /// reviewable evidence. Gel, Solid, and PEG count as IAT-like phases.
    /// This is not a diagnosis.
    /// </summary>
    public static class ReactionPatternClassifier
    {
        public const string Cold = "Cold";
        public const string Warm = "Warm";
        public const string MixedPhase = "MixedPhase";
        public const string Panreactive = "Panreactive";

        private static readonly HashSet<string> IatPhases = new(StringComparer.OrdinalIgnoreCase)
        {
            "AHG", "Gel", "Solid", "PEG", "IAT"
        };

        public static PatternCellObservation Observe(Reaction rxn, RunContext? ctx = null)
        {
            if (string.Equals(rxn.CellNumber, "AC", StringComparison.OrdinalIgnoreCase))
                return new PatternCellObservation(rxn.CellNumber, false, false, false, false);

            var cold = InterpretablePositive(ctx, "IS", rxn.IS)
                       || InterpretablePositive(ctx, "RT", Extra(rxn, "RT"));
            var iat = InterpretablePositive(ctx, "AHG", rxn.AHG);
            foreach (var (phase, value) in rxn.ExtraPhases)
            {
                if (IatPhases.Contains(phase))
                    iat |= InterpretablePositive(ctx, phase, value);
            }
            var any = cold || iat
                      || InterpretablePositive(ctx, "C37", rxn.C37)
                      || rxn.ExtraPhases.Any(kv => InterpretablePositive(ctx, kv.Key, kv.Value));
            var evaluated = HasEntered(rxn);
            return new PatternCellObservation(rxn.CellNumber, evaluated, any, cold, iat);
        }

        public static List<ReactionPatternNote> Classify(IEnumerable<PatternCellObservation> cells)
        {
            var evaluated = cells
                .Where(c => c.Evaluated && !IsAc(c.CellNumber))
                .ToList();
            var notes = new List<ReactionPatternNote>();
            if (evaluated.Count < 3) return notes;

            Add(notes, Cold, evaluated, c => c.ColdPhaseReactive && !c.IatPhaseReactive,
                (n, total) =>
                    $"{n} of {total} evaluated cells react at IS/RT and are negative at AHG/Gel/Solid. " +
                    "This resembles a cold-reactive pattern. Do not treat those cells as AHG-negative rule-outs. Not a diagnosis.");
            Add(notes, Warm, evaluated, c => c.IatPhaseReactive && !c.ColdPhaseReactive,
                (n, total) =>
                    $"{n} of {total} evaluated cells react at AHG/Gel/Solid/PEG without IS/RT reactivity. " +
                    "This resembles a warm (IAT) pattern. Not a diagnosis.");
            Add(notes, MixedPhase, evaluated, c => c.ColdPhaseReactive && c.IatPhaseReactive,
                (n, total) =>
                    $"{n} of {total} evaluated cells react at both IS/RT and AHG/Gel/Solid. " +
                    "This may be mixed cold and warm reactivity. Not a diagnosis.");

            var pan = evaluated.Count(c => c.AnyReactive);
            if (evaluated.Count >= 4 && pan * 5 >= evaluated.Count * 4)
            {
                notes.Add(new ReactionPatternNote
                {
                    Kind = Panreactive,
                    MatchingCells = pan,
                    EvaluatedCells = evaluated.Count,
                    Explanation =
                        $"{pan} of {evaluated.Count} evaluated cells are reactive. " +
                        "This resembles panagglutination (autoantibody, high-prevalence antigen, or reagent issue). " +
                        "Review the autocontrol and DAT. Not a diagnosis."
                });
            }
            return notes;
        }

        private static void Add(
            List<ReactionPatternNote> notes,
            string kind,
            List<PatternCellObservation> evaluated,
            Func<PatternCellObservation, bool> match,
            Func<int, int, string> explain)
        {
            var n = evaluated.Count(match);
            if (n < 3 || n * 2 < evaluated.Count) return;
            notes.Add(new ReactionPatternNote
            {
                Kind = kind,
                MatchingCells = n,
                EvaluatedCells = evaluated.Count,
                Explanation = explain(n, evaluated.Count)
            });
        }

        private static bool InterpretablePositive(RunContext? ctx, string phase, string? value) =>
            (ctx == null || ctx.IsPhaseInterpretable(phase)) && ReactionGrade.IsPositive(value);

        private static string Extra(Reaction rxn, string phase) =>
            rxn.ExtraPhases.TryGetValue(phase, out var value) ? value : "NT";

        private static bool HasEntered(Reaction rxn) =>
            ReactionRowEntered(rxn.IS) || ReactionRowEntered(rxn.C37) || ReactionRowEntered(rxn.AHG)
            || rxn.ExtraPhases.Values.Any(ReactionRowEntered);

        private static bool ReactionRowEntered(string? value) =>
            !string.IsNullOrEmpty(value) && !ReactionGrade.IsNotTested(value);

        private static bool IsAc(string cellNumber) =>
            string.Equals(cellNumber, "AC", StringComparison.OrdinalIgnoreCase);
    }
}
