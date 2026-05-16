using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Distributions;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Antibody identification engine (mirrors analysis.py).
    /// Uses Fisher's Exact Test via MathNet.Numerics.
    /// </summary>
    public class AntibodyAnalyzer
    {
        private readonly DatabaseService _db;

        public AntibodyAnalyzer(DatabaseService db) => _db = db;

        // ── Public entry point ────────────────────────────────────────────────

        /// <param name="updateDb">
        /// When true (default), persists results to the database and updates last_analyzed_at.
        /// Pass false to compute results for display only (e.g. auto-loading a previous analysis).
        /// </param>
        public AnalysisResult AnalyzeSpecimen(string specimenId, bool updateDb = true)
        {
            var reactions = _db.GetAllSpecimenReactions(specimenId);
            var empty = new AnalysisResult { SpecimenId = specimenId };
            if (reactions.Count == 0) return empty;

            var rules = _db.GetAllRules();
            var ruledOut = CalculateRuleouts(specimenId, reactions, rules);
            var (suspected, suspectedStats) = CalculateProbabilities(reactions, ruledOut);
            var patterns = PatternMatching(reactions, ruledOut);
            var detailedRuleouts = GetDetailedRuleouts(specimenId, reactions, rules);
            var suspectedEvidence = GetSuspectedAntibodyEvidence(specimenId, reactions, suspected);
            var combinations = DetectAntibodyCombinations(reactions, suspected);
            var phaseProbabilities = CalculatePhaseSpecificProbabilities(reactions, ruledOut);
            var dosageEffects = DetectDosageEffects(reactions, suspected);

            if (updateDb) UpdateSpecimenAnalysis(specimenId, ruledOut, suspected);

            var result = new AnalysisResult
            {
                SpecimenId = specimenId,
                RuledOut = ruledOut,
                Suspected = suspected,
                SuspectedStatistics = suspectedStats,
                PatternMatches = patterns,
                DetailedRuleouts = detailedRuleouts,
                SuspectedEvidence = suspectedEvidence,
                Combinations = combinations,
                PhraseProbabilities = phaseProbabilities,
                DosageEffects = dosageEffects,
            };
            result.Suggestions = GenerateSuggestions(result);
            return result;
        }

        // ── Rule-outs ─────────────────────────────────────────────────────────

        private Dictionary<string, int> CalculateRuleouts(
            string specimenId, List<Reaction> reactions, List<Rule> rules)
        {
            var ruledOut = new Dictionary<string, int>();
            var byPanel = GroupByPanel(reactions);

            foreach (var (panelId, panelReactions) in byPanel)
            {
                var cellDict = _db.GetPanelCells(panelId)
                    .ToDictionary(c => c.CellNumber);

                foreach (var rxn in panelReactions)
                {
                    if (rxn.CellNumber == "AC") continue;
                    if (!rxn.IsNegative) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in AntigenConstants.Antigens)
                    {
                        if (cell.GetAntigen(ag) != "+") continue;
                        var antibody = $"anti-{ag}";
                        if (CanRuleOut(ag, cell, rules))
                        {
                            ruledOut.TryGetValue(antibody, out var cnt);
                            ruledOut[antibody] = cnt + 1;
                        }
                    }
                }
            }
            return ruledOut;
        }

        private Dictionary<string, List<RuleoutDetail>> GetDetailedRuleouts(
            string specimenId, List<Reaction> reactions, List<Rule> rules)
        {
            var result = new Dictionary<string, List<RuleoutDetail>>();
            var byPanel = GroupByPanel(reactions);

            foreach (var (panelId, panelReactions) in byPanel)
            {
                var panel = _db.GetPanel(panelId);
                var panelName = panel?.Name ?? $"Panel {panelId}";
                var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);

                foreach (var rxn in panelReactions)
                {
                    if (rxn.CellNumber == "AC") continue;
                    if (!rxn.IsNegative) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in AntigenConstants.Antigens)
                    {
                        if (cell.GetAntigen(ag) != "+") continue;
                        if (!CanRuleOut(ag, cell, rules)) continue;

                        var antibody = $"anti-{ag}";
                        var antithetical = AntigenConstants.AntitheticalPairs.TryGetValue(ag, out var at) ? at : null;
                        var antitheticalVal = antithetical != null ? cell.GetAntigen(antithetical) : null;
                        var isHomo = antithetical != null && antitheticalVal == "-";

                        if (!result.ContainsKey(antibody)) result[antibody] = new();
                        result[antibody].Add(new RuleoutDetail
                        {
                            PanelId = panelId,
                            PanelName = panelName,
                            CellNumber = rxn.CellNumber,
                            Antigen = ag,
                            AntigenValue = "+",
                            Antithetical = antithetical,
                            AntitheticalValue = antitheticalVal,
                            IsHomozygous = isHomo,
                            IS = rxn.IS,
                            C37 = rxn.C37,
                            AHG = rxn.AHG,
                            CC = rxn.CC,
                        });
                    }
                }
            }
            return result;
        }

        private bool CanRuleOut(string antigen, PanelCell cell, List<Rule> rules)
        {
            var agVal = cell.GetAntigen(antigen);
            if (agVal != "+") return false;

            // Antigens without an antithetical partner can always be ruled out on a positive cell
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;

            var antitheticalVal = cell.GetAntigen(antithetical);
            var isHomozygous = antitheticalVal == "-";

            // A custom rule can explicitly permit heterozygous rule-out for this antigen.
            // Match on ExceptionAntigen == antigen (e.g. "K")
            // OR on Antibody == "anti-{antigen}" when ExceptionAntigen was left blank.
            if (RuleAllowsHeterozygous(antigen, rules))
                return true;

            // Default: homozygous expression required
            return isHomozygous;
        }

        private static bool RuleAllowsHeterozygous(string antigen, List<Rule> rules)
        {
            foreach (var rule in rules)
            {
                if (!rule.HeterozygousOk) continue;
                if (rule.ExceptionAntigen == antigen) return true;
                if (string.IsNullOrEmpty(rule.ExceptionAntigen) &&
                    string.Equals(rule.Antibody, $"anti-{antigen}", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── Probabilities (Fisher's Exact Test) ────────────────────────────────

        private (Dictionary<string, double> suspected, Dictionary<string, SuspectedStatistics> stats)
            CalculateProbabilities(List<Reaction> reactions, Dictionary<string, int> ruledOut)
        {
            var suspected = new Dictionary<string, double>();
            var stats = new Dictionary<string, SuspectedStatistics>();
            var byPanel = GroupByPanel(reactions);

            foreach (var ag in AntigenConstants.Antigens)
            {
                var antibody = $"anti-{ag}";
                int posWithAg = 0, posWithoutAg = 0, negWithAg = 0, negWithoutAg = 0;

                foreach (var (panelId, panelReactions) in byPanel)
                {
                    var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                    foreach (var rxn in panelReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool agPresent = cell.GetAntigen(ag) == "+";
                        bool isPos = rxn.IsPositive;
                        if (isPos && agPresent) posWithAg++;
                        else if (isPos && !agPresent) posWithoutAg++;
                        else if (!isPos && agPresent) negWithAg++;
                        else negWithoutAg++;
                    }
                }

                int total = posWithAg + posWithoutAg + negWithAg + negWithoutAg;
                if (total == 0 || posWithAg == 0) continue;

                try
                {
                    var pvalue = FisherExactOneSided(posWithAg, posWithoutAg, negWithAg, negWithoutAg);
                    double fisherComp = pvalue < 0.5 ? 1 - pvalue : 0.0;
                    double patternScore = (posWithAg + posWithoutAg) > 0
                        ? (double)posWithAg / (posWithAg + posWithoutAg) : 0;
                    double combined = posWithAg > 0
                        ? (fisherComp + patternScore) / 2 : fisherComp;

                    if (combined <= 0.5) continue;

                    bool include;
                    if (ruledOut.TryGetValue(antibody, out var ruleoutCnt))
                        include = combined > 0.95 && posWithoutAg == 0 && ruleoutCnt <= 2 && posWithAg >= 3;
                    else
                        include = true;

                    if (!include) continue;
                    double rounded = Math.Round(combined, 3);
                    suspected[antibody] = rounded;
                    stats[antibody] = new SuspectedStatistics
                    {
                        FisherPValue = Math.Round(pvalue, 4),
                        PatternScore = Math.Round(patternScore, 4),
                        FisherComponent = Math.Round(fisherComp, 4),
                        CombinedScore = rounded
                    };
                }
                catch { /* skip */ }
            }
            return (suspected, stats);
        }

        // ── Pattern matching ──────────────────────────────────────────────────

        private List<PatternMatch> PatternMatching(
            List<Reaction> reactions, Dictionary<string, int> ruledOut)
        {
            var patterns = new List<PatternMatch>();
            var byPanel = GroupByPanel(reactions);

            foreach (var ag in AntigenConstants.Antigens)
            {
                var antibody = $"anti-{ag}";
                if (ruledOut.ContainsKey(antibody)) continue;

                int matches = 0, mismatches = 0;
                foreach (var (panelId, panelReactions) in byPanel)
                {
                    var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                    foreach (var rxn in panelReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        if (!rxn.IsPositive) continue;
                        if (cell.GetAntigen(ag) == "+") matches++; else mismatches++;
                    }
                }
                if (matches <= 0) continue;
                double confidence = (matches + mismatches) > 0
                    ? Math.Round((double)matches / (matches + mismatches), 3) : 0;
                patterns.Add(new PatternMatch
                {
                    Antibody = antibody,
                    Matches = matches,
                    Mismatches = mismatches,
                    Confidence = confidence
                });
            }
            patterns.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return patterns;
        }

        // ── Suspected evidence ────────────────────────────────────────────────

        private Dictionary<string, SuspectedEvidence> GetSuspectedAntibodyEvidence(
            string specimenId, List<Reaction> reactions, Dictionary<string, double> suspected)
        {
            var evidence = new Dictionary<string, SuspectedEvidence>();
            var byPanel = GroupByPanel(reactions);

            foreach (var (antibody, probability) in suspected)
            {
                var ag = antibody.Replace("anti-", "");
                if (!AntigenConstants.Antigens.Contains(ag)) continue;

                var supporting = new List<EvidenceCell>();
                var conflicting = new List<EvidenceCell>();

                foreach (var (panelId, panelReactions) in byPanel)
                {
                    var panel = _db.GetPanel(panelId);
                    var panelName = panel?.Name ?? $"Panel {panelId}";
                    var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);

                    foreach (var rxn in panelReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool agPresent = cell.GetAntigen(ag) == "+";
                        bool isPos = rxn.IsPositive;

                        if (!isPos && !agPresent) continue;
                        if (!isPos) continue;

                        var ec = new EvidenceCell
                        {
                            PanelId = panelId,
                            PanelName = panelName,
                            CellNumber = rxn.CellNumber,
                            IS = rxn.IS,
                            C37 = rxn.C37,
                            AHG = rxn.AHG,
                            CC = rxn.CC,
                        };
                        (ec.StrongestPhase, ec.StrongestValue) = GetStrongestPhase(rxn);

                        if (agPresent) supporting.Add(ec); else conflicting.Add(ec);
                    }
                }
                int totalPos = supporting.Count + conflicting.Count;
                evidence[antibody] = new SuspectedEvidence
                {
                    Probability = probability,
                    SupportingCells = supporting,
                    ConflictingCells = conflicting,
                    PatternQuality = totalPos > 0 ? Math.Round((double)supporting.Count / totalPos, 3) : 0,
                    TotalSupporting = supporting.Count,
                    TotalConflicting = conflicting.Count
                };
            }
            return evidence;
        }

        // ── Antibody combinations ─────────────────────────────────────────────

        private List<AntibodyCombination> DetectAntibodyCombinations(
            List<Reaction> reactions, Dictionary<string, double> suspected)
        {
            var combos = new List<AntibodyCombination>();
            if (suspected.Count < 2) return combos;

            var top = suspected.OrderByDescending(x => x.Value).Take(5).ToList();
            var byPanel = GroupByPanel(reactions);

            for (int i = 0; i < top.Count; i++)
            {
                for (int j = i + 1; j < top.Count; j++)
                {
                    var (ab1, p1) = top[i];
                    var (ab2, p2) = top[j];
                    var ag1 = ab1.Replace("anti-", "");
                    var ag2 = ab2.Replace("anti-", "");
                    if (!AntigenConstants.Antigens.Contains(ag1) || !AntigenConstants.Antigens.Contains(ag2)) continue;

                    int both = 0, ab1only = 0, ab2only = 0, neither = 0;
                    foreach (var (panelId, panelReactions) in byPanel)
                    {
                        var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                        foreach (var rxn in panelReactions)
                        {
                            if (rxn.CellNumber == "AC") continue;
                            if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                            if (!rxn.IsPositive) continue;
                            bool h1 = cell.GetAntigen(ag1) == "+";
                            bool h2 = cell.GetAntigen(ag2) == "+";
                            if (h1 && h2) both++;
                            else if (h1) ab1only++;
                            else if (h2) ab2only++;
                            else neither++;
                        }
                    }
                    int totalPos = both + ab1only + ab2only + neither;
                    if (totalPos <= 0 || both == 0) continue;
                    double score = Math.Round((double)(both + ab1only + ab2only) / totalPos, 3);
                    if (score > 0.5)
                        combos.Add(new AntibodyCombination
                        {
                            Antibodies = new() { ab1, ab2 },
                            Probabilities = new() { p1, p2 },
                            BothSupport = both,
                            Ab1Only = ab1only,
                            Ab2Only = ab2only,
                            Neither = neither,
                            CombinationScore = score
                        });
                }
            }
            combos.Sort((a, b) => b.CombinationScore.CompareTo(a.CombinationScore));
            return combos;
        }

        // ── Phase-specific probabilities ──────────────────────────────────────

        private Dictionary<string, Dictionary<string, double>> CalculatePhaseSpecificProbabilities(
            List<Reaction> reactions, Dictionary<string, int> ruledOut)
        {
            var result = new Dictionary<string, Dictionary<string, double>>();
            var phases = new[] { "IS", "C37", "AHG", "CC" };
            var byPanel = GroupByPanel(reactions);

            foreach (var phase in phases)
            {
                result[phase] = new();
                foreach (var ag in AntigenConstants.Antigens)
                {
                    var antibody = $"anti-{ag}";
                    int posWithAg = 0, posWithoutAg = 0, negWithAg = 0, negWithoutAg = 0;

                    foreach (var (panelId, panelReactions) in byPanel)
                    {
                        var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                        foreach (var rxn in panelReactions)
                        {
                            if (rxn.CellNumber == "AC") continue;
                            if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                            bool agPresent = cell.GetAntigen(ag) == "+";
                            var phaseVal = GetPhaseValue(rxn, phase);
                            bool phasePos = phaseVal != "NT" && phaseVal != "0" && !string.IsNullOrEmpty(phaseVal);
                            if (phasePos && agPresent) posWithAg++;
                            else if (phasePos && !agPresent) posWithoutAg++;
                            else if (!phasePos && agPresent) negWithAg++;
                            else negWithoutAg++;
                        }
                    }

                    int total = posWithAg + posWithoutAg + negWithAg + negWithoutAg;
                    if (total == 0 || posWithAg == 0) continue;

                    try
                    {
                        var pvalue = FisherExactOneSided(posWithAg, posWithoutAg, negWithAg, negWithoutAg);
                        double prob = pvalue < 0.5 ? 1 - pvalue : 0.0;
                        if (posWithAg > 0 && (posWithAg + posWithoutAg) > 0)
                        {
                            double ps = (double)posWithAg / (posWithAg + posWithoutAg);
                            prob = (prob + ps) / 2;
                        }
                        if (prob > 0.3 && posWithAg > 0)
                            result[phase][antibody] = Math.Round(prob, 3);
                    }
                    catch { }
                }
            }
            return result;
        }

        // ── Dosage effects ────────────────────────────────────────────────────

        private List<DosageEffect> DetectDosageEffects(
            List<Reaction> reactions, Dictionary<string, double> suspected)
        {
            var warnings = new List<DosageEffect>();
            var byPanel = GroupByPanel(reactions);

            foreach (var (antibody, prob) in suspected)
            {
                if (prob < 0.6) continue;
                var ag = antibody.Replace("anti-", "");
                if (!AntigenConstants.AntitheticalPairs.TryGetValue(ag, out var antithetical)) continue;

                var homoRxns = new List<double>();
                var hetRxns = new List<double>();

                foreach (var (panelId, panelReactions) in byPanel)
                {
                    var cellDict = _db.GetPanelCells(panelId).ToDictionary(c => c.CellNumber);
                    foreach (var rxn in panelReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool isHomo = cell.GetAntigen(ag) == "+" && cell.GetAntigen(antithetical) == "-";
                        bool isHet = cell.GetAntigen(ag) == "+" && cell.GetAntigen(antithetical) == "+";
                        if (!isHomo && !isHet) continue;
                        double str = ReactionToNumeric(GetStrongestPhase(rxn).value);
                        if (isHomo) homoRxns.Add(str); else hetRxns.Add(str);
                    }
                }

                if (homoRxns.Count > 0 && hetRxns.Count > 0)
                {
                    double avgH = homoRxns.Average(), avgT = hetRxns.Average();
                    if (avgH > avgT + 0.5)
                        warnings.Add(new DosageEffect
                        {
                            Antibody = antibody,
                            Antigen = ag,
                            AvgHomozygous = Math.Round(avgH, 2),
                            AvgHeterozygous = Math.Round(avgT, 2),
                            HomozygousCount = homoRxns.Count,
                            HeterozygousCount = hetRxns.Count,
                            Severity = (avgH - avgT) > 1.0 ? "high" : "medium"
                        });
                }
            }
            return warnings;
        }

        // ── Suggestions ───────────────────────────────────────────────────────

        private List<string> GenerateSuggestions(AnalysisResult result)
        {
            var critical = new List<string>();
            var important = new List<string>();
            var informational = new List<string>();

            foreach (var ab in result.RuledOut.Keys)
                if (result.Suspected.ContainsKey(ab))
                    critical.Add($"WARNING: {ab} is both ruled out and suspected. " +
                        "This may indicate weak antigen expression or testing issues.");

            foreach (var (ab, ev) in result.SuspectedEvidence)
                if (ev.TotalConflicting > 0)
                {
                    var ag = ab.Replace("anti-", "");
                    important.Add($"{ab} has {ev.TotalConflicting} positive reaction(s) on {ag}-negative cells. " +
                        "This suggests either multiple antibodies or dosage effect.");
                }

            foreach (var (ab, ev) in result.SuspectedEvidence)
                if (ev.Probability > 0.7 && ev.PatternQuality < 0.8)
                    important.Add($"{ab} has high support score ({ev.Probability * 100:F1}%) but imperfect pattern fit. " +
                        "Consider additional testing to confirm.");

            if (result.Suspected.Count > 1)
            {
                var top2 = result.Suspected.OrderByDescending(x => x.Value).Take(2).Select(x => x.Key);
                important.Add($"Pattern suggests multiple antibodies ({string.Join(", ", top2)}). " +
                    "Consider adsorption/elution studies.");
            }

            foreach (var de in result.DosageEffects)
                important.Add($"{de.Antibody} shows dosage effect (homozygous avg: {de.AvgHomozygous:F2}, " +
                    $"heterozygous avg: {de.AvgHeterozygous:F2}). Consider additional homozygous {de.Antigen}+ cells.");

            if (result.PatternMatches.Count > 0)
            {
                var best = result.PatternMatches[0];
                if (best.Confidence >= 1.0 && best.Matches >= 3)
                    informational.Add($"Perfect pattern match for {best.Antibody} " +
                        $"({best.Matches} matches, 0 mismatches). This is a strong identification.");
            }

            var common = new[] { "anti-D", "anti-E", "anti-K", "anti-c", "anti-C" };
            foreach (var ab in common)
            {
                if (result.RuledOut.ContainsKey(ab) || result.Suspected.ContainsKey(ab)) continue;
                informational.Add($"{ab} is not ruled out or suspected. " +
                    "Consider adding appropriate cells if ruling out this antibody is required.");
                break;
            }

            return critical.Concat(important).Concat(informational)
                .Distinct().ToList();
        }

        // ── Update DB after analysis ───────────────────────────────────────────

        private void UpdateSpecimenAnalysis(string specimenId,
            Dictionary<string, int> ruledOut, Dictionary<string, double> suspected)
        {
            _db.ClearSpecimenAntibodies(specimenId);
            _db.ClearSpecimenRuleouts(specimenId);
            foreach (var (ab, cnt) in ruledOut)
                _db.AddSpecimenRuleout(specimenId, ab, cnt);
            foreach (var (ab, prob) in suspected)
                _db.AddSpecimenAntibody(specimenId, ab, prob);
            _db.SetSpecimenLastAnalyzed(specimenId);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Dictionary<int, List<Reaction>> GroupByPanel(List<Reaction> reactions)
        {
            var dict = new Dictionary<int, List<Reaction>>();
            foreach (var r in reactions)
            {
                if (!dict.ContainsKey(r.PanelId)) dict[r.PanelId] = new();
                dict[r.PanelId].Add(r);
            }
            return dict;
        }

        private static string GetPhaseValue(Reaction r, string phase) => phase switch
        {
            "IS" => r.IS,
            "C37" => r.C37,
            "AHG" => r.AHG,
            "CC" => r.CC,
            _ => "NT"
        };

        private static (string phase, string value) GetStrongestPhase(Reaction r)
        {
            var phases = new[] { ("IS", r.IS), ("C37", r.C37), ("AHG", r.AHG), ("CC", r.CC) };
            string bestPhase = "", bestVal = "0";
            foreach (var (ph, val) in phases)
            {
                if (val == "NT" || val == "0" || string.IsNullOrEmpty(val)) continue;
                if (ReactionToNumeric(val) > ReactionToNumeric(bestVal))
                {
                    bestVal = val;
                    bestPhase = ph;
                }
            }
            return (bestPhase, bestVal);
        }

        private static double ReactionToNumeric(string reaction)
        {
            if (string.IsNullOrEmpty(reaction) || reaction == "NT" || reaction == "0") return 0;
            if (int.TryParse(reaction.Replace("+", ""), out int n)) return n;
            return 0;
        }

        /// <summary>
        /// One-sided Fisher's Exact Test p-value for the 2×2 table
        /// [[a, b], [c, d]] testing alternative="greater" (mirroring scipy).
        /// </summary>
        private static double FisherExactOneSided(int a, int b, int c, int d)
        {
            // Uses hypergeometric distribution
            int n = a + b + c + d;
            int r1 = a + b, r2 = c + d;
            int c1 = a + c;

            // P-value = sum of probabilities for tables as extreme or more extreme
            double pValue = 0;
            int kMin = Math.Max(0, r1 + c1 - n);
            int kMax = Math.Min(r1, c1);

            double pA = HypergeometricPmf(n, c1, r1, a);
            for (int k = a; k <= kMax; k++)
                pValue += HypergeometricPmf(n, c1, r1, k);

            return Math.Min(1.0, Math.Max(0.0, pValue));
        }

        private static double HypergeometricPmf(int n, int K, int draws, int k)
        {
            if (k < Math.Max(0, draws + K - n) || k > Math.Min(draws, K))
                return 0;
            return Math.Exp(
                LogBinom(K, k) + LogBinom(n - K, draws - k) - LogBinom(n, draws));
        }

        private static double LogBinom(int n, int k)
        {
            if (k < 0 || k > n) return double.NegativeInfinity;
            return LogFactorial(n) - LogFactorial(k) - LogFactorial(n - k);
        }

        private static double LogFactorial(int n)
        {
            double r = 0;
            for (int i = 2; i <= n; i++) r += Math.Log(i);
            return r;
        }
    }
}
