using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public enum NeutralizationSubstance
    {
        Unknown,
        Plasma,
        Urine,
        Saliva,
        P1
    }

    public readonly record struct NeutralizationCell(
        string CellNumber,
        bool IatReactive,
        bool NeutralizedReactive,
        bool HasNeutralizedGrade);

    /// <summary>
    /// Reads neutralization substance and paired IAT vs Neut/Inhib grades.
    /// Does not invent results or identify an antibody.
    /// </summary>
    public static class NeutralizationParser
    {
        public static readonly IReadOnlyList<string> PhaseNames = new[]
        {
            "Neut", "Neutral", "Neutralized", "Inhib", "Inhibition"
        };

        public static NeutralizationSubstance ParseSubstance(params string?[] texts)
        {
            var blob = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
            if (blob.Length == 0) return NeutralizationSubstance.Unknown;
            if (blob.Contains("urine") || blob.Contains("sda") || blob.Contains("cad"))
                return NeutralizationSubstance.Urine;
            if (blob.Contains("saliva") || blob.Contains("lewis") || blob.Contains("le(a") || blob.Contains("le(b"))
                return NeutralizationSubstance.Saliva;
            if (blob.Contains("p1") || blob.Contains("hydatid") || blob.Contains("pigeon"))
                return NeutralizationSubstance.P1;
            if (blob.Contains("plasma") || blob.Contains("c4") || blob.Contains("chido") ||
                blob.Contains("rodgers") || ContainsWord(blob, "ch") || ContainsWord(blob, "rg"))
                return NeutralizationSubstance.Plasma;
            return NeutralizationSubstance.Unknown;
        }

        public static bool MentionsNeutralization(params string?[] texts)
        {
            var blob = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
            return blob.Contains("neutral") || blob.Contains("inhibited") || blob.Contains("inhibition");
        }

        public static bool IsNeutralizationPhase(string? name) =>
            !string.IsNullOrWhiteSpace(name) &&
            PhaseNames.Any(p => string.Equals(p, name.Trim(), StringComparison.OrdinalIgnoreCase));

        public static NeutralizationCell Observe(Reaction rxn)
        {
            var iat = ExtraPhaseParser.IsIatLike("AHG") && ReactionGrade.IsPositive(rxn.AHG);
            foreach (var (phase, value) in rxn.ExtraPhases)
            {
                if (IsNeutralizationPhase(phase)) continue;
                if (ExtraPhaseParser.IsIatLike(phase) && ReactionGrade.IsPositive(value))
                    iat = true;
            }

            string? neut = null;
            foreach (var (phase, value) in rxn.ExtraPhases)
            {
                if (!IsNeutralizationPhase(phase)) continue;
                neut = value;
                break;
            }

            var hasNeut = !string.IsNullOrWhiteSpace(neut) && !ReactionGrade.IsNotTested(neut);
            return new NeutralizationCell(
                rxn.CellNumber,
                iat,
                hasNeut && ReactionGrade.IsPositive(neut),
                hasNeut);
        }

        public static string SubstanceLabel(NeutralizationSubstance substance) => substance switch
        {
            NeutralizationSubstance.Plasma => "plasma / C4 (Ch/Rg-like)",
            NeutralizationSubstance.Urine => "urine (Sd(a)-like)",
            NeutralizationSubstance.Saliva => "saliva (Lewis-like)",
            NeutralizationSubstance.P1 => "P1 substance",
            _ => "the recorded neutralizing substance"
        };

        /// <summary>
        /// Antigens whose absence on an unused cell may help evaluate a
        /// neutralization / soluble-substance pattern. Not a diagnosis list.
        /// </summary>
        public static IReadOnlyList<string> DiscriminatorAntigens(NeutralizationSubstance substance) =>
            substance switch
            {
                NeutralizationSubstance.Plasma => new[] { "Ch", "Rg" },
                NeutralizationSubstance.Urine => new[] { "Sda" },
                NeutralizationSubstance.Saliva => new[] { "Lea", "Leb" },
                NeutralizationSubstance.P1 => new[] { "P1" },
                _ => new[] { "Ch", "Rg", "Sda", "P1" }
            };

        public static bool FavorsSolubleSubstanceFollowUp(AnalysisResult result)
        {
            var note = result.ReactionPatterns.FirstOrDefault(p => p.Kind == NeutralizationNotes.Kind);
            if (note == null) return false;
            if (note.Explanation.Contains("argues against", StringComparison.OrdinalIgnoreCase))
                return false;
            return note.Explanation.Contains("became nonreactive", StringComparison.OrdinalIgnoreCase)
                   || note.Explanation.Contains("qualitative note", StringComparison.OrdinalIgnoreCase)
                   || note.Explanation.Contains("favors a soluble-substance", StringComparison.OrdinalIgnoreCase);
        }

        public static NeutralizationSubstance SubstanceOf(AnalysisResult result)
        {
            var note = result.ReactionPatterns.FirstOrDefault(p => p.Kind == NeutralizationNotes.Kind);
            return note == null ? NeutralizationSubstance.Unknown : ParseSubstance(note.Explanation);
        }

        private static bool ContainsWord(string blob, string word) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                blob, $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b");
    }
}
