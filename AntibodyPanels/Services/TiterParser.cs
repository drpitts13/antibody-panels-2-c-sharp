using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Reads a recorded antibody titer from notes or extra-phase columns.
    /// Does not invent a titer or identify an antibody.
    /// </summary>
    public static class TiterParser
    {
        public const int TypicalHtlaMinimum = 16;

        private static readonly Regex TiterWord = new(
            @"\btiter(?:\s*(?:of|=|:))?\s*(?:1\s*[:/]\s*)?(\d{1,5})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Reciprocal = new(
            @"\b1\s*[:/]\s*(\d{1,5})\b",
            RegexOptions.Compiled);

        private static readonly HashSet<string> TiterPhaseNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Titer", "Dilution", "Dil", "Diln"
        };

        public static int? Highest(string? notes, IEnumerable<KeyValuePair<string, string>>? extraPhases = null)
        {
            int? best = null;
            foreach (var value in ValuesFromNotes(notes))
                best = Max(best, value);
            if (extraPhases == null) return best;
            foreach (var (phase, raw) in extraPhases)
            {
                if (TiterPhaseNames.Contains(phase.Trim()))
                {
                    var parsed = FromPhaseValue(raw);
                    if (parsed != null)
                        best = Max(best, parsed.Value);
                    continue;
                }
                if (!ExtraPhaseParser.TryParseDilution(phase, out var dilution))
                    continue;
                if (ReactionGrade.IsPositive(raw))
                    best = Max(best, dilution);
            }
            return best;
        }

        public static bool SuggestsHtlaTiter(int? titer) =>
            titer >= TypicalHtlaMinimum;

        private static IEnumerable<int> ValuesFromNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) yield break;
            foreach (Match m in TiterWord.Matches(notes))
            {
                if (int.TryParse(m.Groups[1].Value, out var n) && n > 0)
                    yield return n;
            }
            if (TiterWord.IsMatch(notes)) yield break;
            foreach (Match m in Reciprocal.Matches(notes))
            {
                if (int.TryParse(m.Groups[1].Value, out var n) && n > 1)
                    yield return n;
            }
        }

        private static int? FromPhaseValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || ReactionGrade.IsNotTested(raw))
                return null;
            var text = raw.Trim();
            if (text.Contains('+') || text.Equals("MF", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("H", StringComparison.OrdinalIgnoreCase) || text is "0" or "N")
                return null;
            if (int.TryParse(text, out var n) && n >= 4)
                return n;
            var m = Reciprocal.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var rec) && rec > 1)
                return rec;
            return null;
        }

        private static int Max(int? current, int next) =>
            current == null || next > current.Value ? next : current.Value;
    }
}
