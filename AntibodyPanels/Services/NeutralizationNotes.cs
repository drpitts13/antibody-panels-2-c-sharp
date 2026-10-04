using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Labels recorded neutralization as reviewable evidence.
    /// This is not a diagnosis and does not change suspected or ruled-out lists.
    /// </summary>
    public static class NeutralizationNotes
    {
        public const string Kind = "Neutralization";

        public static void Apply(
            AnalysisResult result,
            string? notes,
            IEnumerable<Reaction> reactions,
            IEnumerable<PanelRun>? runs = null)
        {
            var runLabels = (runs ?? Enumerable.Empty<PanelRun>())
                .Select(r => r.DisplayLabel)
                .ToArray();
            var substance = NeutralizationParser.ParseSubstance(
                new[] { notes }.Concat(runLabels).ToArray());
            var mentioned = NeutralizationParser.MentionsNeutralization(
                new[] { notes }.Concat(runLabels).ToArray());

            var cells = reactions
                .Where(r => !string.Equals(r.CellNumber, "AC", System.StringComparison.OrdinalIgnoreCase))
                .Select(NeutralizationParser.Observe)
                .Where(c => c.IatReactive && c.HasNeutralizedGrade)
                .ToList();

            if (cells.Count >= 3)
            {
                var lost = cells.Count(c => !c.NeutralizedReactive);
                var persisted = cells.Count(c => c.NeutralizedReactive);
                var label = NeutralizationParser.SubstanceLabel(substance);
                string explanation;
                if (lost * 2 >= cells.Count)
                {
                    explanation =
                        $"{lost} of {cells.Count} IAT-reactive cells became nonreactive after neutralization with {label}. " +
                        "This favors a soluble-substance / HTLA-like specificity over a typical warm alloantibody. Not a diagnosis.";
                    StrengthenHtla(result);
                }
                else
                {
                    explanation =
                        $"{persisted} of {cells.Count} IAT-reactive cells remained reactive after neutralization with {label}. " +
                        "This argues against a readily neutralized HTLA (Ch/Rg, Sd(a), Lewis). Not a diagnosis.";
                }

                result.ReactionPatterns.Add(new ReactionPatternNote
                {
                    Kind = Kind,
                    MatchingCells = lost * 2 >= cells.Count ? lost : persisted,
                    EvaluatedCells = cells.Count,
                    Explanation = explanation
                });
                return;
            }

            if (mentioned)
            {
                result.ReactionPatterns.Add(new ReactionPatternNote
                {
                    Kind = Kind,
                    MatchingCells = 0,
                    EvaluatedCells = 0,
                    Explanation =
                        $"Notes record neutralization with {NeutralizationParser.SubstanceLabel(substance)}. " +
                        "No paired IAT vs Neut/Inhib grades were entered, so this is a qualitative note only. Not a diagnosis."
                });
            }
        }

        private static void StrengthenHtla(AnalysisResult result)
        {
            var htla = result.ReactionPatterns.Find(p => p.Kind == ReactionPatternClassifier.Htla);
            if (htla == null) return;
            const string extra =
                " Neutralization of IAT reactivity further supports an HTLA-like / soluble-substance pattern. Not a diagnosis.";
            if (!htla.Explanation.Contains("Neutralization of IAT", System.StringComparison.Ordinal))
                htla.Explanation = htla.Explanation.TrimEnd() + extra;
        }
    }
}
