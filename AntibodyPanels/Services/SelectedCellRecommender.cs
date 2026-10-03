using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Ranks unused inventory cells by how well they discriminate remaining
    /// candidate antibodies. Recommendations are decision support, not a call.
    /// </summary>
    public static class SelectedCellRecommender
    {
        public const int MaxRecommendations = 5;

        public static List<SelectedCellRecommendation> Recommend(
            AnalysisResult result,
            IReadOnlyCollection<(int PanelId, string CellNumber)> alreadyTested,
            IEnumerable<(Panel Panel, PanelCell Cell)> inventory)
        {
            var candidates = ResolveCandidates(result);
            if (candidates.Count == 0) return new();

            var tested = new HashSet<(int, string)>(alreadyTested);
            var scored = new List<SelectedCellRecommendation>();

            foreach (var (panel, cell) in inventory)
            {
                if (cell.CellNumber == "AC") continue;
                if (tested.Contains((panel.PanelId, cell.CellNumber))) continue;
                var rec = ScoreCell(cell, panel, candidates, result);
                if (rec != null && rec.Score > 0)
                    scored.Add(rec);
            }

            return scored
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.PanelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.CellNumber, StringComparer.Ordinal)
                .Take(MaxRecommendations)
                .ToList();
        }

        internal static List<string> ResolveCandidates(AnalysisResult result)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ab in result.Suspected.Keys.OrderByDescending(k => result.Suspected[k]))
            {
                if (result.RuledOut.ContainsKey(ab)) continue;
                if (seen.Add(ab)) list.Add(ab);
            }

            if (list.Count == 0)
            {
                foreach (var pm in result.PatternMatches.Take(4))
                {
                    if (result.RuledOut.ContainsKey(pm.Antibody)) continue;
                    if (seen.Add(pm.Antibody)) list.Add(pm.Antibody);
                }
            }

            return list.Take(4).ToList();
        }

        private static SelectedCellRecommendation? ScoreCell(
            PanelCell cell, Panel panel, List<string> candidates, AnalysisResult result)
        {
            var present = new List<string>();
            var absent = new List<string>();
            var reasons = new List<string>();
            int score = 0;

            foreach (var antibody in candidates)
            {
                var ag = AntigenOf(antibody);
                if (!cell.HasTypedAntigen(ag)) continue;
                if (cell.GetAntigen(ag) == "+")
                {
                    present.Add(antibody);
                    var homo = IsHomozygous(cell, ag);
                    score += homo ? 3 : 1;
                    if (NeedsMoreAgPositive(result, antibody))
                    {
                        score += 1;
                        reasons.Add(homo
                            ? $"homozygous {ag}+ may add a reactive cell for {antibody}"
                            : $"{ag}+ may add a reactive cell for {antibody}");
                    }
                    else if (homo)
                    {
                        reasons.Add($"homozygous {ag}+ expression for {antibody}");
                    }
                }
                else
                {
                    absent.Add(antibody);
                    if (NeedsMoreAgNegative(result, antibody))
                    {
                        score += 1;
                        reasons.Add($"{ag}- may add a nonreactive cell for {antibody}");
                    }
                }
            }

            if (present.Count == 0 && absent.Count == 0)
                return null;

            foreach (var plus in present)
            {
                foreach (var minus in absent)
                {
                    score += 4;
                    var pair = $"{plus} vs {minus}";
                    if (!reasons.Exists(r => r.Contains(pair, StringComparison.Ordinal)))
                        reasons.Insert(0, $"distinguishes {pair} ({AntigenOf(plus)}+ {AntigenOf(minus)}-)");
                }
            }

            if (present.Count == 1 && absent.Count > 0)
            {
                score += 2;
                reasons.Add($"expresses {AntigenOf(present[0])} and lacks competing {string.Join(", ", absent.Select(AntigenOf))}");
            }

            if (score <= 0) return null;

            var profile = string.Join(" ", candidates
                .Select(ab => AntigenOf(ab))
                .Distinct()
                .Where(cell.HasTypedAntigen)
                .Select(ag => ag + cell.GetAntigen(ag)));

            var distinguishes = present
                .SelectMany(plus => absent.Select(minus => $"{plus} vs {minus}"))
                .Distinct()
                .ToList();

            var explanation = FormatExplanation(panel, cell, reasons, distinguishes);
            return new SelectedCellRecommendation
            {
                PanelId = panel.PanelId,
                PanelName = panel.Name,
                LotNumber = panel.LotNumber,
                CellNumber = cell.CellNumber,
                Score = score,
                AntigenProfile = profile,
                Distinguishes = distinguishes,
                Explanation = explanation,
            };
        }

        private static string FormatExplanation(Panel panel, PanelCell cell,
            List<string> reasons, List<string> distinguishes)
        {
            var where = string.IsNullOrWhiteSpace(panel.Name)
                ? $"Cell {cell.CellNumber}"
                : $"{panel.Name} cell {cell.CellNumber}";
            if (distinguishes.Count > 0)
            {
                var pairs = string.Join(" and ", distinguishes.Take(2));
                var extra = reasons.FirstOrDefault(r => !r.StartsWith("distinguishes", StringComparison.Ordinal));
                return extra == null
                    ? $"{where} may help distinguish {pairs}."
                    : $"{where} may help distinguish {pairs} because it {extra}.";
            }

            if (reasons.Count == 0)
                return $"{where} types remaining candidate antigens.";
            return $"{where} is useful because it {reasons[0]}.";
        }

        private static bool IsHomozygous(PanelCell cell, string antigen)
        {
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return false;
            return cell.HasTypedAntigen(antithetical) && cell.GetAntigen(antithetical) == "-";
        }

        private static bool NeedsMoreAgPositive(AnalysisResult result, string antibody) =>
            result.SuspectedStatistics.TryGetValue(antibody, out var stats) &&
            !stats.MeetsIdentificationRule &&
            stats.PositiveAgPositiveCount < stats.IdentificationRequired;

        private static bool NeedsMoreAgNegative(AnalysisResult result, string antibody) =>
            result.SuspectedStatistics.TryGetValue(antibody, out var stats) &&
            !stats.MeetsIdentificationRule &&
            stats.NegativeAgNegativeCount < stats.IdentificationRequired;

        private static string AntigenOf(string antibody) =>
            antibody.StartsWith("anti-", StringComparison.OrdinalIgnoreCase)
                ? antibody[5..]
                : antibody;
    }
}
