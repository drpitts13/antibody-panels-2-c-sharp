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
        bool IatPhaseReactive,
        double IatStrength);

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
        public const string Htla = "HTLA";
        public const string HighPrevalence = "HighPrevalence";
        public const string Autoantibody = "Autoantibody";
        public const string LowFrequency = "LowFrequency";

        private static readonly HashSet<string> IatPhases = new(StringComparer.OrdinalIgnoreCase)
        {
            "AHG", "Gel", "Solid", "PEG", "IAT"
        };

        public static PatternCellObservation Observe(Reaction rxn, RunContext? ctx = null)
        {
            var cold = InterpretablePositive(ctx, "IS", rxn.IS)
                       || InterpretablePositive(ctx, "RT", Extra(rxn, "RT"));
            var iat = InterpretablePositive(ctx, "AHG", rxn.AHG);
            var iatStrength = InterpretablePositive(ctx, "AHG", rxn.AHG) ? ReactionGrade.Strength(rxn.AHG) : 0;
            foreach (var (phase, value) in rxn.ExtraPhases)
            {
                if (!IatPhases.Contains(phase) || !InterpretablePositive(ctx, phase, value)) continue;
                iat = true;
                iatStrength = Math.Max(iatStrength, ReactionGrade.Strength(value));
            }
            var any = cold || iat
                      || InterpretablePositive(ctx, "C37", rxn.C37)
                      || rxn.ExtraPhases.Any(kv => InterpretablePositive(ctx, kv.Key, kv.Value));
            return new PatternCellObservation(rxn.CellNumber, HasEntered(rxn), any, cold, iat, iatStrength);
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

            Add(notes, Htla, evaluated,
                c => c.IatPhaseReactive && !c.ColdPhaseReactive && c.IatStrength > 0 && c.IatStrength <= 1,
                (n, total) =>
                    $"{n} of {total} evaluated cells show weak IAT reactivity (w+ or 1+) without IS/RT. " +
                    "This resembles an HTLA or high-titer low-avidity pattern. Consider titration, neutralization, and rare Ag− cells. Not a diagnosis.",
                minMatches: 4);

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
                var ac = cells.FirstOrDefault(c => IsAc(c.CellNumber) && c.Evaluated);
                if (!string.IsNullOrEmpty(ac.CellNumber))
                {
                    if (ac.AnyReactive)
                    {
                        notes.Add(new ReactionPatternNote
                        {
                            Kind = Autoantibody,
                            MatchingCells = pan,
                            EvaluatedCells = evaluated.Count,
                            Explanation =
                                "Autocontrol is reactive with a panagglutinin pattern. " +
                                "This favors autoantibody (or recently transfused cells) over a high-prevalence alloantibody. Not a diagnosis."
                        });
                    }
                    else
                    {
                        notes.Add(new ReactionPatternNote
                        {
                            Kind = HighPrevalence,
                            MatchingCells = pan,
                            EvaluatedCells = evaluated.Count,
                            Explanation =
                                "Autocontrol is nonreactive with a panagglutinin pattern. " +
                                "This favors a high-prevalence alloantibody over a warm autoantibody. Consider rare Ag− cells. Not a diagnosis."
                        });
                    }
                }
            }
            else if (evaluated.Count >= 6 && pan is 1 or 2)
            {
                notes.Add(new ReactionPatternNote
                {
                    Kind = LowFrequency,
                    MatchingCells = pan,
                    EvaluatedCells = evaluated.Count,
                    Explanation =
                        $"{pan} of {evaluated.Count} evaluated cells are reactive. " +
                        "This may be a low-frequency antigen, an extra antibody, or a mistype. Not a diagnosis."
                });
            }
            return notes;
        }

        private static void Add(
            List<ReactionPatternNote> notes,
            string kind,
            List<PatternCellObservation> evaluated,
            Func<PatternCellObservation, bool> match,
            Func<int, int, string> explain,
            int minMatches = 3)
        {
            var n = evaluated.Count(match);
            if (n < minMatches || n * 2 < evaluated.Count) return;
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
