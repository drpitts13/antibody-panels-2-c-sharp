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
    /// Labels cold, warm/IAT, mixed-phase, panreactive, HTLA-like, autoantibody,
    /// high-prevalence, and low-frequency patterns as reviewable evidence.
    /// Gel, Solid, and PEG count as IAT-like phases. Scores are not a diagnosis.
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

        public static PatternCellObservation Observe(Reaction rxn, RunContext? ctx = null)
        {
            var cold = InterpretablePositive(ctx, "IS", rxn.IS)
                       || InterpretablePositive(ctx, "RT", Extra(rxn, "RT"));
            var iat = InterpretablePositive(ctx, "AHG", rxn.AHG);
            var iatStrength = InterpretablePositive(ctx, "AHG", rxn.AHG) ? ReactionGrade.Strength(rxn.AHG) : 0;
            foreach (var (phase, value) in rxn.ExtraPhases)
            {
                if (!ExtraPhaseParser.IsIatLike(phase) || !InterpretablePositive(ctx, phase, value)) continue;
                iat = true;
                iatStrength = Math.Max(iatStrength, ReactionGrade.Strength(value));
            }
            var any = cold || iat
                      || InterpretablePositive(ctx, "C37", rxn.C37)
                      || rxn.ExtraPhases.Any(kv => InterpretablePositive(ctx, kv.Key, kv.Value));
            return new PatternCellObservation(rxn.CellNumber, HasEntered(rxn), any, cold, iat, iatStrength);
        }

        public static List<ReactionPatternNote> Classify(
            IEnumerable<PatternCellObservation> cells, string? datResult = null)
        {
            var all = cells.ToList();
            var evaluated = all
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

            var reactive = evaluated.Where(c => c.AnyReactive).ToList();
            var nonreactive = evaluated.Where(c => !c.AnyReactive).ToList();
            var pan = reactive.Count;
            if (evaluated.Count >= 4 && pan * 5 >= evaluated.Count * 4)
            {
                var panNote = new ReactionPatternNote
                {
                    Kind = Panreactive,
                    MatchingCells = pan,
                    EvaluatedCells = evaluated.Count,
                    SupportingCells = reactive.Select(c => c.CellNumber).ToList(),
                    ConflictingCells = nonreactive.Select(c => c.CellNumber).ToList(),
                    EvidenceScore = Score(pan, evaluated.Count),
                    Explanation =
                        $"{pan} of {evaluated.Count} evaluated cells are reactive. " +
                        "This resembles panagglutination (autoantibody, high-prevalence antigen, or reagent issue). " +
                        "Review the autocontrol and DAT. Not a diagnosis."
                };
                if (nonreactive.Count > 0)
                {
                    panNote.Explanation =
                        panNote.Explanation.TrimEnd() +
                        $" Nonreactive cell(s): {string.Join(", ", nonreactive.Select(c => c.CellNumber))} " +
                        "(conflicts with a complete panagglutinin).";
                }
                AppendScore(panNote);
                notes.Add(panNote);

                var ac = all.FirstOrDefault(c => IsAc(c.CellNumber) && c.Evaluated);
                var acPresent = !string.IsNullOrEmpty(ac.CellNumber);
                if (acPresent)
                {
                    if (ac.AnyReactive)
                    {
                        notes.Add(DatAwareNote(
                            Autoantibody, pan, evaluated.Count, reactive, nonreactive, datResult,
                            "Autocontrol is reactive with a panagglutinin pattern. " +
                            "This favors autoantibody (or recently transfused cells) over a high-prevalence alloantibody. Not a diagnosis.",
                            favorsAuto: true));
                    }
                    else
                    {
                        notes.Add(DatAwareNote(
                            HighPrevalence, pan, evaluated.Count, reactive, nonreactive, datResult,
                            "Autocontrol is nonreactive with a panagglutinin pattern. " +
                            "This favors a high-prevalence alloantibody over a warm autoantibody. Consider rare Ag− cells. Not a diagnosis.",
                            favorsAuto: false));
                    }
                }
                else if (IsDatPositive(datResult))
                {
                    notes.Add(DatAwareNote(
                        Autoantibody, pan, evaluated.Count, reactive, nonreactive, datResult,
                        "Autocontrol was not recorded. A positive DAT with panagglutination favors autoantibody " +
                        "over a high-prevalence alloantibody, but does not replace an autocontrol. Not a diagnosis.",
                        favorsAuto: true));
                }
                else if (IsDatNegative(datResult))
                {
                    notes.Add(DatAwareNote(
                        HighPrevalence, pan, evaluated.Count, reactive, nonreactive, datResult,
                        "Autocontrol was not recorded. A negative DAT with panagglutination favors a " +
                        "high-prevalence alloantibody over a typical warm autoantibody. Consider rare Ag− cells. Not a diagnosis.",
                        favorsAuto: false));
                }
            }
            else if (evaluated.Count >= 6 && pan is 1 or 2)
            {
                var lfa = new ReactionPatternNote
                {
                    Kind = LowFrequency,
                    MatchingCells = pan,
                    EvaluatedCells = evaluated.Count,
                    SupportingCells = reactive.Select(c => c.CellNumber).ToList(),
                    EvidenceScore = Score(pan, evaluated.Count),
                    Explanation =
                        $"{pan} of {evaluated.Count} evaluated cells are reactive" +
                        (reactive.Count > 0
                            ? $" (cell {string.Join(", ", reactive.Select(c => c.CellNumber))})"
                            : "") +
                        ". This may be a low-frequency antigen, an extra antibody, or a mistype. Not a diagnosis."
                };
                AppendScore(lfa);
                notes.Add(lfa);
            }
            return notes;
        }

        public static bool IsDatPositive(string? dat)
        {
            if (string.IsNullOrWhiteSpace(dat)) return false;
            var v = dat.Trim();
            if (v.Equals("NT", StringComparison.OrdinalIgnoreCase)) return false;
            if (IsDatNegative(v)) return false;
            return ReactionGrade.IsPositive(v);
        }

        public static bool IsDatNegative(string? dat)
        {
            if (string.IsNullOrWhiteSpace(dat)) return false;
            var v = dat.Trim();
            return v.Equals("Negative", StringComparison.OrdinalIgnoreCase)
                   || ReactionGrade.IsNegative(v);
        }

        private static ReactionPatternNote DatAwareNote(
            string kind, int matching, int evaluated,
            List<PatternCellObservation> supporting,
            List<PatternCellObservation> conflicting,
            string? datResult, string baseExplanation, bool favorsAuto)
        {
            var note = new ReactionPatternNote
            {
                Kind = kind,
                MatchingCells = matching,
                EvaluatedCells = evaluated,
                SupportingCells = supporting.Select(c => c.CellNumber).ToList(),
                ConflictingCells = conflicting.Select(c => c.CellNumber).ToList(),
                EvidenceScore = Score(matching, evaluated),
                Explanation = baseExplanation
            };
            if (IsDatPositive(datResult))
            {
                var dat = datResult!.Trim();
                if (favorsAuto)
                {
                    note.Explanation = note.Explanation.TrimEnd() +
                        $" DAT is {dat}, which supports autoantibody over a high-prevalence alloantibody.";
                    note.EvidenceScore = Math.Min(1, note.EvidenceScore + 0.1);
                }
                else
                {
                    note.Explanation = note.Explanation.TrimEnd() +
                        $" DAT is {dat}, which argues against a typical high-prevalence alloantibody " +
                        "(review recently transfused cells or a recording error).";
                    note.ConflictingCells.Add("DAT");
                    note.EvidenceScore = Math.Max(0.2, note.EvidenceScore - 0.15);
                }
            }
            else if (IsDatNegative(datResult))
            {
                if (favorsAuto)
                {
                    note.Explanation = note.Explanation.TrimEnd() +
                        " DAT is negative, which argues against a typical warm autoantibody despite the autocontrol.";
                    note.ConflictingCells.Add("DAT");
                    note.EvidenceScore = Math.Max(0.2, note.EvidenceScore - 0.15);
                }
                else
                {
                    note.Explanation = note.Explanation.TrimEnd() +
                        " DAT is negative, which agrees with a high-prevalence alloantibody over a warm autoantibody.";
                    note.EvidenceScore = Math.Min(1, note.EvidenceScore + 0.1);
                }
            }
            AppendScore(note);
            return note;
        }

        private static void Add(
            List<ReactionPatternNote> notes,
            string kind,
            List<PatternCellObservation> evaluated,
            Func<PatternCellObservation, bool> match,
            Func<int, int, string> explain,
            int minMatches = 3)
        {
            var matching = evaluated.Where(match).ToList();
            var n = matching.Count;
            if (n < minMatches || n * 2 < evaluated.Count) return;
            var note = new ReactionPatternNote
            {
                Kind = kind,
                MatchingCells = n,
                EvaluatedCells = evaluated.Count,
                SupportingCells = matching.Select(c => c.CellNumber).ToList(),
                EvidenceScore = Score(n, evaluated.Count),
                Explanation = explain(n, evaluated.Count)
            };
            AppendScore(note);
            notes.Add(note);
        }

        private static double Score(int matching, int evaluated) =>
            evaluated <= 0 ? 0 : Math.Round(Math.Clamp(matching / (double)evaluated, 0, 1), 2);

        private static void AppendScore(ReactionPatternNote note)
        {
            if (note.Explanation.Contains("evidence score", StringComparison.OrdinalIgnoreCase))
                return;
            note.Explanation = note.Explanation.TrimEnd() +
                               $" Analytical evidence score {note.EvidenceScore:0.00}.";
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
