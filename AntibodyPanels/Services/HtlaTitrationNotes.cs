using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Strengthens an HTLA pattern note when a titer was recorded.
    /// Still evidence, not a diagnosis.
    /// </summary>
    public static class HtlaTitrationNotes
    {
        private const string GenericAdvice =
            "Consider titration, neutralization, and rare Ag− cells. Not a diagnosis.";

        public static void Apply(AnalysisResult result, int? titer)
        {
            var htla = result.ReactionPatterns
                .Find(p => p.Kind == ReactionPatternClassifier.Htla);
            if (htla == null || titer == null) return;

            string replacement;
            if (TiterParser.SuggestsHtlaTiter(titer))
            {
                replacement =
                    $"A recorded titer of {titer} with weak IAT supports an HTLA-like high-titer pattern. " +
                    "Neutralization and rare Ag− cells remain reviewable. Not a diagnosis.";
            }
            else
            {
                replacement =
                    $"A recorded titer of {titer} is lower than typical HTLA; " +
                    "consider a weak ordinary antibody or incomplete titration. Not a diagnosis.";
            }

            if (htla.Explanation.Contains(GenericAdvice, System.StringComparison.Ordinal))
                htla.Explanation = htla.Explanation.Replace(GenericAdvice, replacement);
            else if (!htla.Explanation.Contains($"titer of {titer}", System.StringComparison.Ordinal))
                htla.Explanation = htla.Explanation.TrimEnd() + " " + replacement;
        }
    }
}
