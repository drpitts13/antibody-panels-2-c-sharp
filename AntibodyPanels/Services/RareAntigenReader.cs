using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Finds high-prevalence antigen-negatives on a cell from typed columns
    /// or Special Types text. Missing/NT is not treated as negative.
    /// </summary>
    public static class RareAntigenReader
    {
        private static readonly Regex PairNegative = new(
            @"\b(Yt|Kp|Js|Lu|Co|Wr|Di|Sc|LW)\s*\(\s*a\s*[-−–]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<string> Negatives(PanelCell cell)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ag in AntigenConstants.HighPrevalenceAntigens)
            {
                if (!AntigenConstants.IsWarehouse(ag)) continue;
                if (!cell.HasTypedAntigen(ag) || cell.GetAntigen(ag) != "-") continue;
                if (seen.Add(ag)) found.Add(ag);
            }
            foreach (var ag in FromSpecialTypes(cell.SpecialTypes))
            {
                if (seen.Add(ag)) found.Add(ag);
            }
            return found;
        }

        public static IEnumerable<string> FromSpecialTypes(string? specialTypes)
        {
            if (string.IsNullOrWhiteSpace(specialTypes)) yield break;
            foreach (Match m in PairNegative.Matches(specialTypes))
            {
                var resolved = Resolve(m.Groups[1].Value + "a");
                if (resolved != null) yield return resolved;
            }
            foreach (var name in AntigenConstants.HighPrevalenceAntigens
                         .OrderByDescending(n => n.Length))
            {
                var pattern = $@"(?<![A-Za-z]){Regex.Escape(name)}\s*[-−–]";
                if (name is "k")
                {
                    if (specialTypes.IndexOf("k-", StringComparison.Ordinal) < 0 &&
                        specialTypes.IndexOf("k−", StringComparison.Ordinal) < 0 &&
                        specialTypes.IndexOf("k–", StringComparison.Ordinal) < 0)
                        continue;
                }
                else if (!Regex.IsMatch(specialTypes, pattern))
                {
                    continue;
                }
                yield return name;
            }
        }

        private static string? Resolve(string antigen) =>
            AntigenConstants.HighPrevalenceAntigens
                .FirstOrDefault(a => string.Equals(a, antigen, StringComparison.OrdinalIgnoreCase));
    }
}
