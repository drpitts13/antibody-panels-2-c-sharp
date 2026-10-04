using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        private static readonly Regex SelectedVialHint = new(
            @"\bselect(?:ed)?(?:\s*cells?)?\b|selectogen|0\.8\s*%",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<SelectedCellRecommendation> Recommend(
            AnalysisResult result,
            IReadOnlyCollection<(int PanelId, string CellNumber)> alreadyTested,
            IEnumerable<(Panel Panel, PanelCell Cell)> inventory)
        {
            var candidates = ResolveCandidates(result);
            if (candidates.Count == 0 && !NeedsRareNegativeCell(result) &&
                !NeutralizationParser.FavorsSolubleSubstanceFollowUp(result))
                return new();

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

            var ranked = scored
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.PanelName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.CellNumber, StringComparer.Ordinal)
                .ToList();
            var inDate = ranked.Where(r => !r.IsExpired).Take(MaxRecommendations).ToList();
            if (inDate.Count > 0) return inDate;

            return ranked.Where(r => r.IsExpired).Take(2).ToList();
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

            if (present.Count == 0 && absent.Count == 0 && !NeedsRareNegativeCell(result) &&
                !NeutralizationParser.FavorsSolubleSubstanceFollowUp(result))
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

            ApplyPatientCompatibility(cell, candidates, result, ref score, reasons);

            if (LooksLikeSelectedCellVial(panel))
            {
                score += 2;
                reasons.Add("comes from an unused selected-cell vial");
            }

            ApplyRareNegatives(cell, result, ref score, reasons);
            ApplyNeutralizationDiscriminators(cell, panel, result, ref score, reasons);
            ApplyDating(panel, reasons);

            if (score <= 0) return null;

            var profile = string.Join(" ", candidates
                .Select(ab => AntigenOf(ab))
                .Distinct()
                .Where(cell.HasTypedAntigen)
                .Select(ag => ag + cell.GetAntigen(ag)));
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = string.Join(" ", RareAntigenReader.Negatives(cell)
                    .Select(ag => ag + "-"));
            }

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
                IsExpired = panel.IsExpired,
                IsExpiringSoon = !panel.IsExpired && panel.IsExpiringSoon,
                ExpirationDate = panel.ExpirationDate,
            };
        }

        private static string FormatExplanation(Panel panel, PanelCell cell,
            List<string> reasons, List<string> distinguishes)
        {
            var where = string.IsNullOrWhiteSpace(panel.Name)
                ? $"Cell {cell.CellNumber}"
                : $"{panel.Name} cell {cell.CellNumber}";
            var phenotype = reasons.FirstOrDefault(r => r.StartsWith("patient types", StringComparison.Ordinal));
            var vial = reasons.FirstOrDefault(r => r.Contains("selected-cell vial", StringComparison.Ordinal));
            var dating = reasons.FirstOrDefault(r =>
                r.Contains("expired vial", StringComparison.Ordinal) ||
                r.StartsWith("expires ", StringComparison.Ordinal));
            var core = reasons.FirstOrDefault(r =>
                           r.StartsWith("distinguishes", StringComparison.Ordinal) ||
                           r.Contains("homozygous", StringComparison.Ordinal) ||
                           r.Contains("may add", StringComparison.Ordinal) ||
                           r.Contains("may help evaluate", StringComparison.Ordinal) ||
                           r.StartsWith("expresses", StringComparison.Ordinal) ||
                           r.StartsWith("types ", StringComparison.Ordinal))
                       ?? reasons.FirstOrDefault(r =>
                           r != phenotype && r != vial && r != dating);

            if (distinguishes.Count > 0)
            {
                var pairs = string.Join(" and ", distinguishes.Take(2));
                var extra = phenotype ?? (core != null && !core.StartsWith("distinguishes", StringComparison.Ordinal)
                    ? core : null);
                var sentence = extra == null
                    ? $"{where} may help distinguish {pairs}."
                    : $"{where} may help distinguish {pairs} because it {extra}.";
                if (vial != null)
                    sentence = sentence.TrimEnd('.') + $"; it {vial}.";
                if (dating != null)
                    sentence = sentence.TrimEnd('.') + $"; it {dating}.";
                return sentence;
            }

            var parts = new List<string>();
            if (core != null) parts.Add(core);
            if (phenotype != null && phenotype != core) parts.Add(phenotype);
            if (vial != null && vial != core) parts.Add(vial);
            if (dating != null && dating != core) parts.Add(dating);
            if (parts.Count == 0)
                return $"{where} types remaining candidate antigens.";
            return $"{where} is useful because it {string.Join("; it ", parts)}.";
        }

        private static void ApplyDating(Panel panel, List<string> reasons)
        {
            if (panel.IsExpired)
            {
                reasons.Add("comes from an expired vial and should not be used for additional testing");
                return;
            }
            if (panel.IsExpiringSoon && !string.IsNullOrWhiteSpace(panel.ExpirationDate))
                reasons.Add($"expires {panel.ExpirationDate} — confirm the vial is still in date");
        }

        public static bool NeedsRareNegativeCell(AnalysisResult result) =>
            result.ReactionPatterns.Any(p =>
                p.Kind is ReactionPatternClassifier.Htla or ReactionPatternClassifier.HighPrevalence);

        private static void ApplyRareNegatives(
            PanelCell cell, AnalysisResult result, ref int score, List<string> reasons)
        {
            if (!NeedsRareNegativeCell(result)) return;
            var negatives = RareAntigenReader.Negatives(cell);
            if (negatives.Count == 0) return;
            score += 4;
            var shown = string.Join(", ", negatives.Take(3).Select(ag => ag + "−"));
            reasons.Add($"types {shown} and may help evaluate an HTLA or high-prevalence pattern");
        }

        private static void ApplyNeutralizationDiscriminators(
            PanelCell cell, Panel panel, AnalysisResult result, ref int score, List<string> reasons)
        {
            if (!NeutralizationParser.FavorsSolubleSubstanceFollowUp(result)) return;
            var targets = NeutralizationParser.DiscriminatorAntigens(
                NeutralizationParser.SubstanceOf(result));
            var hits = targets.Where(ag => LacksNeutralizationAntigen(cell, ag)).ToList();
            if (hits.Count == 0) return;
            if (!IsNeutralizationInventoryCell(panel, cell, hits)) return;
            score += 4;
            var shown = string.Join(", ", hits.Take(3).Select(ag => ag + "−"));
            reasons.Add($"types {shown} and may help evaluate neutralization / soluble-substance evidence");
        }

        /// <summary>
        /// Prefer selected-cell vials, Special Types annotations, or warehouse
        /// Ch/Rg types. Ordinary ID-panel P1− / Le(a−) rows are not boosted.
        /// </summary>
        private static bool IsNeutralizationInventoryCell(
            Panel panel, PanelCell cell, IReadOnlyList<string> hits)
        {
            if (LooksLikeSelectedCellVial(panel)) return true;
            if (hits.Any(ag => SpecialTypesMentionsNegative(cell.SpecialTypes, ag)))
                return true;
            return hits.Any(ag =>
                AntigenConstants.IsWarehouse(ag) &&
                cell.HasTypedAntigen(ag) && cell.GetAntigen(ag) == "-");
        }

        private static bool LacksNeutralizationAntigen(PanelCell cell, string antigen)
        {
            if (cell.HasTypedAntigen(antigen) && cell.GetAntigen(antigen) == "-")
                return true;
            return SpecialTypesMentionsNegative(cell.SpecialTypes, antigen);
        }

        private static bool SpecialTypesMentionsNegative(string? specialTypes, string antigen)
        {
            if (string.IsNullOrWhiteSpace(specialTypes)) return false;
            foreach (var name in RareAntigenReader.FromSpecialTypes(specialTypes))
            {
                if (string.Equals(name, antigen, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            var compact = antigen.Equals("Sda", StringComparison.OrdinalIgnoreCase)
                ? @"(?:Sda|Sd\s*\(\s*a\s*)"
                : Regex.Escape(antigen);
            return Regex.IsMatch(specialTypes, $@"(?<![A-Za-z]){compact}\s*[-−–]", RegexOptions.IgnoreCase);
        }

        public static bool LooksLikeSelectedCellVial(Panel panel)
        {
            var text = string.Join(" ", new[] { panel.Name, panel.ProductLine, panel.SpecialNotes }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            return !string.IsNullOrWhiteSpace(text) && SelectedVialHint.IsMatch(text);
        }

        private static void ApplyPatientCompatibility(
            PanelCell cell, List<string> candidates, AnalysisResult result,
            ref int score, List<string> reasons)
        {
            foreach (var antibody in candidates)
            {
                var ag = AntigenOf(antibody);
                if (!cell.HasTypedAntigen(ag) || cell.GetAntigen(ag) != "+") continue;
                var patient = PatientAntigen(result, ag);
                if (patient == "-")
                {
                    score += 2;
                    reasons.Add($"patient types {ag}- so an unused {ag}+ cell can support allo{antibody}");
                }
                else if (patient == "+")
                {
                    score -= 1;
                    reasons.Add($"patient types {ag}+, so this {ag}+ cell is weaker evidence for allo{antibody}");
                }
            }
        }

        public static string? PatientAntigen(AnalysisResult result, string antigen)
        {
            var notes = result.PatientTypingConsiderations
                .Where(c => string.Equals(c.Antigen, antigen, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var predicted = notes.FirstOrDefault(c =>
                c.Kind == PatientTypingKind.Predicted && !string.IsNullOrEmpty(c.PatientValue));
            if (predicted != null)
                return predicted.PatientValue;
            var reliable = notes.FirstOrDefault(c =>
                c.Kind is PatientTypingKind.Supporting or PatientTypingKind.Against);
            return string.IsNullOrEmpty(reliable?.PatientValue) ? null : reliable!.PatientValue;
        }

        private static bool IsHomozygous(PanelCell cell, string antigen) =>
            cell.IsHomozygousFor(antigen);

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
