using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Antibody identification engine.
    /// Operates across all panel runs for a specimen; each run's treatment context
    /// gates which antigens and phases contribute to rule-outs and probability scoring.
    /// </summary>
    public class AntibodyAnalyzer
    {
        private readonly DatabaseService _db;

        public AntibodyAnalyzer(DatabaseService db) => _db = db;

        // ── Public entry point ────────────────────────────────────────────────

        public AnalysisResult AnalyzeSpecimen(string specimenId, bool updateDb = true)
        {
            var reactions = _db.GetAllSpecimenReactions(specimenId);
            var empty = new AnalysisResult { SpecimenId = specimenId };
            if (reactions.Count == 0) return empty;

            var runs = _db.GetAllSpecimenRuns(specimenId);
            var contexts = BuildContexts(runs);
            var antigens = RelevantAntigens(contexts);
            var byRun = GroupByRun(reactions);

            var rules = _db.GetAllRules();
            var ruledOut = CalculateRuleouts(byRun, contexts, antigens, rules, out var gatedRuleouts);
            var (suspected, suspectedStats) = CalculateProbabilities(byRun, contexts, antigens, ruledOut);
            var patterns = PatternMatching(byRun, contexts, antigens, ruledOut);
            var detailedRuleouts = GetDetailedRuleouts(byRun, contexts, antigens, rules);
            var suspectedEvidence = GetSuspectedAntibodyEvidence(byRun, contexts, suspected);
            var combinations = DetectAntibodyCombinations(byRun, contexts, suspected);
            var phaseProbabilities = CalculatePhaseSpecificProbabilities(byRun, contexts, antigens, ruledOut);
            var dosageEffects = DetectDosageEffects(byRun, contexts, suspected);
            var inferences = BuildTreatmentInferences(byRun, contexts, suspected);
            var absorptionConclusions = BuildAbsorptionConclusions(byRun, contexts, suspected);

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
                GatedRuleouts = gatedRuleouts,
                TreatmentInferences = inferences,
                AbsorptionConclusions = absorptionConclusions,
            };
            result.Suggestions = GenerateSuggestions(result);
            return result;
        }

        // ── Context helpers ───────────────────────────────────────────────────

        private Dictionary<int, RunContext> BuildContexts(List<PanelRun> runs)
        {
            var extrasByPanel = new Dictionary<int, List<string>>();
            foreach (var panelId in runs.Select(r => r.PanelId).Distinct())
                extrasByPanel[panelId] = _db.GetPanelExtraAntigens(panelId);

            var dict = new Dictionary<int, RunContext>();
            foreach (var run in runs)
                dict[run.RunId] = new RunContext(run, extrasByPanel[run.PanelId]);
            return dict;
        }

        private static IReadOnlyList<string> RelevantAntigens(Dictionary<int, RunContext> contexts)
        {
            var extras = contexts.Values.SelectMany(c => c.ExtraAntigens);
            return AntigenConstants.GetAnalyzedAntigens(extras);
        }

        private static Dictionary<int, List<Reaction>> GroupByRun(List<Reaction> reactions)
        {
            var dict = new Dictionary<int, List<Reaction>>();
            foreach (var r in reactions)
            {
                if (!dict.ContainsKey(r.RunId)) dict[r.RunId] = new();
                dict[r.RunId].Add(r);
            }
            return dict;
        }

        // ── Rule-outs ─────────────────────────────────────────────────────────

        private Dictionary<string, int> CalculateRuleouts(
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyList<string> antigens,
            List<Rule> rules,
            out List<GatedRuleout> gatedRuleouts)
        {
            var ruledOut = new Dictionary<string, int>();
            gatedRuleouts = new List<GatedRuleout>();
            var gatedSeen = new HashSet<string>(); // prevent duplicate gate messages

            foreach (var (runId, runReactions) in byRun)
            {
                if (!contexts.TryGetValue(runId, out var ctx)) continue;
                var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);

                foreach (var rxn in runReactions)
                {
                    if (rxn.CellNumber == "AC") continue;
                    if (!ctx.IsNegative(rxn)) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in antigens)
                    {
                        if (!ctx.TypesAntigen(ag)) continue;
                        if (cell.GetAntigen(ag) != "+") continue;
                        var antibody = $"anti-{ag}";

                        // Check whether the treatment destroys this antigen on the cell.
                        // If so, a negative reaction is uninformative for this antibody.
                        if (!ctx.CanContributeRuleout(ag, cell))
                        {
                            var gateKey = $"{antibody}|{ctx.Run.CellTreatment}";
                            if (gatedSeen.Add(gateKey))
                                gatedRuleouts.Add(new GatedRuleout
                                {
                                    Antibody = antibody,
                                    Antigen = ag,
                                    CellTreatmentLabel =
                                        AntigenTreatmentEffects.GetDisplayName(ctx.Run.CellTreatment),
                                    Reason = $"{ag} is destroyed by {ctx.Run.CellTreatment}; " +
                                             "negative reactions cannot rule out this antibody.",
                                });
                            continue;
                        }

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
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyList<string> antigens,
            List<Rule> rules)
        {
            var result = new Dictionary<string, List<RuleoutDetail>>();

            foreach (var (runId, runReactions) in byRun)
            {
                if (!contexts.TryGetValue(runId, out var ctx)) continue;
                var panel = _db.GetPanel(ctx.Run.PanelId);
                var panelName = panel?.Name ?? $"Panel {ctx.Run.PanelId}";
                var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);

                foreach (var rxn in runReactions)
                {
                    if (rxn.CellNumber == "AC") continue;
                    if (!ctx.IsNegative(rxn)) continue;
                    if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;

                    foreach (var ag in antigens)
                    {
                        if (!ctx.TypesAntigen(ag)) continue;
                        if (cell.GetAntigen(ag) != "+") continue;
                        if (!ctx.CanContributeRuleout(ag, cell)) continue;
                        if (!CanRuleOut(ag, cell, rules)) continue;

                        var antibody = $"anti-{ag}";
                        var antithetical = AntigenConstants.AntitheticalPairs
                            .TryGetValue(ag, out var at) ? at : null;
                        var antitheticalTyped = antithetical != null && cell.HasTypedAntigen(antithetical);
                        var antitheticalVal = antitheticalTyped ? cell.GetAntigen(antithetical!) : null;
                        var isHomo = antitheticalTyped && antitheticalVal == "-";

                        if (!result.ContainsKey(antibody)) result[antibody] = new();
                        result[antibody].Add(new RuleoutDetail
                        {
                            RunId = runId,
                            RunLabel = ctx.Run.DisplayLabel,
                            PanelId = ctx.Run.PanelId,
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
            if (cell.GetAntigen(antigen) != "+") return false;
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var antithetical))
                return true;
            if (!cell.HasTypedAntigen(antithetical))
                return false;
            var antitheticalVal = cell.GetAntigen(antithetical);
            var isHomozygous = antitheticalVal == "-";
            if (RuleAllowsHeterozygous(antigen, rules)) return true;
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

        // ── Probabilities (Fisher's Exact Test) ───────────────────────────────

        private (Dictionary<string, double>, Dictionary<string, SuspectedStatistics>)
            CalculateProbabilities(
                Dictionary<int, List<Reaction>> byRun,
                Dictionary<int, RunContext> contexts,
                IReadOnlyList<string> antigens,
                Dictionary<string, int> ruledOut)
        {
            var suspected = new Dictionary<string, double>();
            var stats = new Dictionary<string, SuspectedStatistics>();

            foreach (var ag in antigens)
            {
                var antibody = $"anti-{ag}";
                double posWithAg = 0, posWithoutAg = 0, negWithAg = 0, negWithoutAg = 0;
                var posAgPosCells = new HashSet<(int PanelId, string CellNumber)>();
                var negAgNegCells = new HashSet<(int PanelId, string CellNumber)>();

                foreach (var (runId, runReactions) in byRun)
                {
                    if (!contexts.TryGetValue(runId, out var ctx)) continue;
                    if (!ctx.TypesAntigen(ag)) continue;
                    // Skip this run for this antigen if the treatment destroys it
                    if (AntigenTreatmentEffects.GetCellEffect(ctx.Run.CellTreatment, ag)
                            == AntigenEffect.Destroyed)
                        continue;

                    var weight = ctx.EvidenceWeight;
                    var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);

                    foreach (var rxn in runReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool agPresent = ctx.IsAntigenPresent(cell, ag);
                        bool isPos = ctx.IsPositive(rxn);
                        if (isPos && agPresent)
                        {
                            posWithAg += weight;
                            posAgPosCells.Add((ctx.Run.PanelId, rxn.CellNumber));
                        }
                        else if (isPos && !agPresent) posWithoutAg += weight;
                        else if (!isPos && agPresent) negWithAg += weight;
                        else negWithoutAg += weight;

                        if (!isPos && !agPresent && ctx.IsNegative(rxn))
                            negAgNegCells.Add((ctx.Run.PanelId, rxn.CellNumber));
                    }
                }

                double total = posWithAg + posWithoutAg + negWithAg + negWithoutAg;
                if (total == 0 || posWithAg == 0) continue;

                try
                {
                    var pvalue = FisherExactOneSided(
                        (int)Math.Round(posWithAg), (int)Math.Round(posWithoutAg),
                        (int)Math.Round(negWithAg), (int)Math.Round(negWithoutAg));
                    double fisherComp = pvalue < 0.5 ? 1 - pvalue : 0.0;
                    double patternScore = (posWithAg + posWithoutAg) > 0
                        ? posWithAg / (posWithAg + posWithoutAg) : 0;
                    double combined = posWithAg > 0
                        ? (fisherComp + patternScore) / 2 : fisherComp;

                    if (combined <= AppSettings.Current.ProbabilityThreshold) continue;

                    bool include;
                    if (ruledOut.TryGetValue(antibody, out var ruleoutCnt))
                        include = combined > 0.95 && posWithoutAg == 0 && ruleoutCnt <= 2 && posWithAg >= 3;
                    else
                        include = true;

                    if (!include) continue;
                    double rounded = Math.Round(combined, 3);
                    int required = AppSettings.Current.IdentificationCellCount;
                    if (required < 1 || required > 3) required = 3;
                    int posCount = posAgPosCells.Count;
                    int negCount = negAgNegCells.Count;
                    suspected[antibody] = rounded;
                    stats[antibody] = new SuspectedStatistics
                    {
                        FisherPValue = Math.Round(pvalue, 4),
                        PatternScore = Math.Round(patternScore, 4),
                        FisherComponent = Math.Round(fisherComp, 4),
                        CombinedScore = rounded,
                        PositiveAgPositiveCount = posCount,
                        NegativeAgNegativeCount = negCount,
                        IdentificationRequired = required,
                        MeetsIdentificationRule = posCount >= required && negCount >= required
                    };
                }
                catch { /* skip */ }
            }
            return (suspected, stats);
        }

        // ── Pattern matching ──────────────────────────────────────────────────

        private List<PatternMatch> PatternMatching(
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyList<string> antigens,
            Dictionary<string, int> ruledOut)
        {
            var patterns = new List<PatternMatch>();

            foreach (var ag in antigens)
            {
                var antibody = $"anti-{ag}";
                if (ruledOut.ContainsKey(antibody)) continue;

                int matches = 0, mismatches = 0;
                foreach (var (runId, runReactions) in byRun)
                {
                    if (!contexts.TryGetValue(runId, out var ctx)) continue;
                    if (!ctx.TypesAntigen(ag)) continue;
                    if (AntigenTreatmentEffects.GetCellEffect(ctx.Run.CellTreatment, ag)
                            == AntigenEffect.Destroyed)
                        continue;

                    var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);
                    foreach (var rxn in runReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        if (!ctx.IsPositive(rxn)) continue;
                        if (ctx.IsAntigenPresent(cell, ag)) matches++; else mismatches++;
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
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            Dictionary<string, double> suspected)
        {
            var evidence = new Dictionary<string, SuspectedEvidence>();

            foreach (var (antibody, probability) in suspected)
            {
                var ag = antibody.Replace("anti-", "");
                if (!AntigenConstants.IsKnown(ag)) continue;

                var supporting = new List<EvidenceCell>();
                var conflicting = new List<EvidenceCell>();

                foreach (var (runId, runReactions) in byRun)
                {
                    if (!contexts.TryGetValue(runId, out var ctx)) continue;
                    if (!ctx.TypesAntigen(ag)) continue;
                    if (AntigenTreatmentEffects.GetCellEffect(ctx.Run.CellTreatment, ag)
                            == AntigenEffect.Destroyed)
                        continue;

                    var panel = _db.GetPanel(ctx.Run.PanelId);
                    var panelName = panel?.Name ?? $"Panel {ctx.Run.PanelId}";
                    var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);

                    foreach (var rxn in runReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool agPresent = ctx.IsAntigenPresent(cell, ag);
                        bool isPos = ctx.IsPositive(rxn);
                        if (!isPos) continue;

                        var (sp, sv) = ctx.GetStrongestPhase(rxn);
                        var ec = new EvidenceCell
                        {
                            RunId = runId,
                            RunLabel = ctx.Run.DisplayLabel,
                            PanelId = ctx.Run.PanelId,
                            PanelName = panelName,
                            CellNumber = rxn.CellNumber,
                            IS = rxn.IS,
                            C37 = rxn.C37,
                            AHG = rxn.AHG,
                            CC = rxn.CC,
                            StrongestPhase = sp,
                            StrongestValue = sv,
                        };
                        if (agPresent) supporting.Add(ec); else conflicting.Add(ec);
                    }
                }
                int totalPos = supporting.Count + conflicting.Count;
                evidence[antibody] = new SuspectedEvidence
                {
                    Probability = probability,
                    SupportingCells = supporting,
                    ConflictingCells = conflicting,
                    PatternQuality = totalPos > 0
                        ? Math.Round((double)supporting.Count / totalPos, 3) : 0,
                    TotalSupporting = supporting.Count,
                    TotalConflicting = conflicting.Count
                };
            }
            return evidence;
        }

        // ── Antibody combinations ─────────────────────────────────────────────

        private List<AntibodyCombination> DetectAntibodyCombinations(
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            Dictionary<string, double> suspected)
        {
            var combos = new List<AntibodyCombination>();
            if (suspected.Count < 2) return combos;

            var top = suspected.OrderByDescending(x => x.Value).Take(5).ToList();

            for (int i = 0; i < top.Count; i++)
            {
                for (int j = i + 1; j < top.Count; j++)
                {
                    var (ab1, p1) = top[i];
                    var (ab2, p2) = top[j];
                    var ag1 = ab1.Replace("anti-", "");
                    var ag2 = ab2.Replace("anti-", "");
                    if (!AntigenConstants.IsKnown(ag1) ||
                        !AntigenConstants.IsKnown(ag2)) continue;

                    int both = 0, ab1only = 0, ab2only = 0, neither = 0;
                    foreach (var (runId, runReactions) in byRun)
                    {
                        if (!contexts.TryGetValue(runId, out var ctx)) continue;
                        if (!ctx.TypesAntigen(ag1) || !ctx.TypesAntigen(ag2)) continue;
                        var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);
                        foreach (var rxn in runReactions)
                        {
                            if (rxn.CellNumber == "AC") continue;
                            if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                            if (!ctx.IsPositive(rxn)) continue;
                            bool h1 = ctx.IsAntigenPresent(cell, ag1);
                            bool h2 = ctx.IsAntigenPresent(cell, ag2);
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
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            IReadOnlyList<string> antigens,
            Dictionary<string, int> ruledOut)
        {
            var result = new Dictionary<string, Dictionary<string, double>>();
            // CC is a check-cell control, not an antibody reactivity phase
            var phases = new[] { "IS", "C37", "AHG" };

            foreach (var phase in phases)
            {
                result[phase] = new();
                foreach (var ag in antigens)
                {
                    var antibody = $"anti-{ag}";
                    double posWithAg = 0, posWithoutAg = 0, negWithAg = 0, negWithoutAg = 0;

                    foreach (var (runId, runReactions) in byRun)
                    {
                        if (!contexts.TryGetValue(runId, out var ctx)) continue;
                        if (!ctx.IsPhaseInterpretable(phase)) continue;
                        if (!ctx.TypesAntigen(ag)) continue;
                        if (AntigenTreatmentEffects.GetCellEffect(ctx.Run.CellTreatment, ag)
                                == AntigenEffect.Destroyed)
                            continue;

                        var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);
                        foreach (var rxn in runReactions)
                        {
                            if (rxn.CellNumber == "AC") continue;
                            if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                            bool agPresent = ctx.IsAntigenPresent(cell, ag);
                            var phaseVal = ctx.GetInterpretedPhaseValue(rxn, phase);
                            bool phasePos = phaseVal != "NT" && phaseVal != "0" &&
                                           !string.IsNullOrEmpty(phaseVal);
                            if (phasePos && agPresent) posWithAg++;
                            else if (phasePos && !agPresent) posWithoutAg++;
                            else if (!phasePos && agPresent) negWithAg++;
                            else negWithoutAg++;
                        }
                    }

                    double total = posWithAg + posWithoutAg + negWithAg + negWithoutAg;
                    if (total == 0 || posWithAg == 0) continue;

                    try
                    {
                        var pvalue = FisherExactOneSided(
                            (int)posWithAg, (int)posWithoutAg,
                            (int)negWithAg, (int)negWithoutAg);
                        double prob = pvalue < 0.5 ? 1 - pvalue : 0.0;
                        if (posWithAg > 0 && (posWithAg + posWithoutAg) > 0)
                        {
                            double ps = posWithAg / (posWithAg + posWithoutAg);
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
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            Dictionary<string, double> suspected)
        {
            var warnings = new List<DosageEffect>();

            foreach (var (antibody, prob) in suspected)
            {
                if (prob < 0.6) continue;
                var ag = antibody.Replace("anti-", "");
                if (!AntigenConstants.AntitheticalPairs.TryGetValue(ag, out var antithetical)) continue;

                var homoRxns = new List<double>();
                var hetRxns = new List<double>();

                foreach (var (runId, runReactions) in byRun)
                {
                    if (!contexts.TryGetValue(runId, out var ctx)) continue;
                    if (!ctx.TypesAntigen(ag)) continue;
                    // Use only untreated (or enhanced) runs for dosage analysis
                    if (AntigenTreatmentEffects.GetCellEffect(ctx.Run.CellTreatment, ag)
                            == AntigenEffect.Destroyed)
                        continue;

                    var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);
                    foreach (var rxn in runReactions)
                    {
                        if (rxn.CellNumber == "AC") continue;
                        if (!cellDict.TryGetValue(rxn.CellNumber, out var cell)) continue;
                        bool isHomo = cell.GetAntigen(ag) == "+" &&
                                      cell.HasTypedAntigen(antithetical) &&
                                      cell.GetAntigen(antithetical) == "-";
                        bool isHet = cell.GetAntigen(ag) == "+" &&
                                     cell.HasTypedAntigen(antithetical) &&
                                     cell.GetAntigen(antithetical) == "+";
                        if (!isHomo && !isHet) continue;
                        var (_, sv) = ctx.GetStrongestPhase(rxn);
                        double str = RunContext.ReactionToNumeric(sv);
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

        // ── Treatment inferences ──────────────────────────────────────────────

        private List<TreatmentInference> BuildTreatmentInferences(
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            Dictionary<string, double> suspected)
        {
            var inferences = new List<TreatmentInference>();

            // Find untreated runs for comparison
            var untreatedRuns = contexts.Values
                .Where(c => c.Run.IsUntreated)
                .ToList();
            var treatedRuns = contexts.Values
                .Where(c => !c.Run.IsUntreated)
                .ToList();

            if (!untreatedRuns.Any() || !treatedRuns.Any()) return inferences;

            foreach (var treatedCtx in treatedRuns)
            {
                if (!byRun.TryGetValue(treatedCtx.Run.RunId, out var treatedRxns)) continue;

                // Find the untreated run for the same panel
                var untreatedCtx = untreatedRuns
                    .FirstOrDefault(c => c.Run.PanelId == treatedCtx.Run.PanelId);
                if (untreatedCtx == null) continue;
                if (!byRun.TryGetValue(untreatedCtx.Run.RunId, out var untreatedRxns)) continue;

                var treatedByCell = treatedRxns.ToDictionary(r => r.CellNumber);
                var untreatedByCell = untreatedRxns.ToDictionary(r => r.CellNumber);

                foreach (var (cellNum, treatedRxn) in treatedByCell)
                {
                    if (cellNum == "AC") continue;
                    if (!untreatedByCell.TryGetValue(cellNum, out var untreatedRxn)) continue;

                    bool treatedPos = treatedCtx.IsPositive(treatedRxn);
                    bool untreatedPos = untreatedCtx.IsPositive(untreatedRxn);

                    if (untreatedPos && !treatedPos)
                    {
                        // Reactivity lost on treated cells
                        var type = treatedCtx.Run.CellTreatment switch
                        {
                            CellTreatment.DTT    => TreatmentInferenceType.ReactivityLostOnDTT,
                            CellTreatment.Ficin  => TreatmentInferenceType.ReactivityLostOnEnzyme,
                            CellTreatment.Papain => TreatmentInferenceType.ReactivityLostOnEnzyme,
                            _                    => TreatmentInferenceType.ReactivityLostOnEnzyme,
                        };
                        // Which suspected antibodies does this cell support?
                        var cellDict = _db.GetPanelCells(treatedCtx.Run.PanelId)
                            .ToDictionary(c => c.CellNumber);
                        if (!cellDict.TryGetValue(cellNum, out var cell)) continue;

                        foreach (var (ab, _) in suspected)
                        {
                            var ag = ab.Replace("anti-", "");
                            var effect = AntigenTreatmentEffects.GetCellEffect(
                                treatedCtx.Run.CellTreatment, ag);
                            if (effect == AntigenEffect.Destroyed && cell.GetAntigen(ag) == "+")
                            {
                                inferences.Add(new TreatmentInference
                                {
                                    RunLabel = treatedCtx.Run.DisplayLabel,
                                    Antibody = ab,
                                    InferenceType = type,
                                    Observation =
                                        $"Cell {cellNum}: reactive untreated, non-reactive on " +
                                        $"{treatedCtx.Run.DisplayLabel} — consistent with {ab} " +
                                        $"(antigen {ag} destroyed by {treatedCtx.Run.CellTreatment}).",
                                });
                            }
                        }
                    }
                }
            }

            return inferences.DistinctBy(i => i.Observation).ToList();
        }

        // ── Absorption conclusions ────────────────────────────────────────────

        private List<AbsorptionConclusion> BuildAbsorptionConclusions(
            Dictionary<int, List<Reaction>> byRun,
            Dictionary<int, RunContext> contexts,
            Dictionary<string, double> suspected)
        {
            var conclusions = new List<AbsorptionConclusion>();

            foreach (var ctx in contexts.Values)
            {
                if (ctx.AbsorbedAntibodies.Count == 0 && !ctx.IsAutoAdsorbed) continue;
                if (!byRun.TryGetValue(ctx.Run.RunId, out var runRxns)) continue;

                var surviving = new List<string>();
                var absorbedOut = new List<string>();

                // Antibodies that are normally present in the untreated suspected set
                foreach (var (ab, _) in suspected)
                {
                    // Was it absorbed?
                    if (ctx.AbsorbedAntibodies.Contains(ab))
                    {
                        // Check if it's still reactive
                        var cellDict = _db.GetPanelCells(ctx.Run.PanelId).ToDictionary(c => c.CellNumber);
                        bool stillReactive = runRxns.Any(r =>
                            r.CellNumber != "AC" &&
                            ctx.IsPositive(r));
                        if (stillReactive) surviving.Add(ab + " (survived absorption)");
                        else absorbedOut.Add(ab);
                    }
                }

                if (surviving.Count > 0 || absorbedOut.Count > 0)
                    conclusions.Add(new AbsorptionConclusion
                    {
                        AbsorptionLabel = ctx.Run.DisplayLabel,
                        AbsorbedOut = absorbedOut,
                        Surviving = surviving,
                    });
            }
            return conclusions;
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
                    important.Add($"{ab} has {ev.TotalConflicting} positive reaction(s) on " +
                        $"{ag}-negative cells. This suggests either multiple antibodies or dosage effect.");
                }

            foreach (var (ab, ev) in result.SuspectedEvidence)
                if (ev.Probability > 0.7 && ev.PatternQuality < 0.8)
                    important.Add($"{ab} has high support score ({ev.Probability * 100:F1}%) but " +
                        "imperfect pattern fit. Consider additional testing to confirm.");

            foreach (var (ab, stats) in result.SuspectedStatistics)
            {
                var ag = ab.Replace("anti-", "");
                var n = stats.IdentificationRequired;
                if (stats.MeetsIdentificationRule)
                {
                    informational.Add($"{ab} meets the {stats.IdentificationRuleLabel} identification rule " +
                        $"({stats.PositiveAgPositiveCount} Ag+ reactive, {stats.NegativeAgNegativeCount} Ag- nonreactive).");
                }
                else
                {
                    if (stats.PositiveAgPositiveCount < n)
                    {
                        var need = n - stats.PositiveAgPositiveCount;
                        important.Add($"{ab} has {stats.PositiveAgPositiveCount} of {n} required {ag}+ reactive cells. " +
                            $"Add {need} more {ag}+ cell(s) that react.");
                    }
                    if (stats.NegativeAgNegativeCount < n)
                    {
                        var need = n - stats.NegativeAgNegativeCount;
                        important.Add($"{ab} has {stats.NegativeAgNegativeCount} of {n} required {ag}- nonreactive cells. " +
                            $"Add {need} more {ag}- cell(s) that do not react.");
                    }
                }
            }

            if (result.Suspected.Count > 1)
            {
                var top2 = result.Suspected.OrderByDescending(x => x.Value).Take(2).Select(x => x.Key);
                important.Add($"Pattern suggests multiple antibodies ({string.Join(", ", top2)}). " +
                    "Consider adsorption/elution studies.");
            }

            foreach (var de in result.DosageEffects)
                important.Add($"{de.Antibody} shows dosage effect (homozygous avg: {de.AvgHomozygous:F2}, " +
                    $"heterozygous avg: {de.AvgHeterozygous:F2}). " +
                    $"Consider additional homozygous {de.Antigen}+ cells.");

            // Special-panel suggestions
            foreach (var gate in result.GatedRuleouts)
                informational.Add($"NOTE: {gate.Antibody} cannot be ruled out from " +
                    $"{gate.CellTreatmentLabel} reactions — {gate.Reason}");

            foreach (var inf in result.TreatmentInferences)
                important.Add(inf.Observation);

            foreach (var abs in result.AbsorptionConclusions)
            {
                if (abs.Surviving.Count > 0)
                    important.Add($"{abs.AbsorptionLabel}: " +
                        $"these specificities survived: {string.Join(", ", abs.Surviving)}.");
                if (abs.AbsorbedOut.Count > 0)
                    informational.Add($"{abs.AbsorptionLabel}: " +
                        $"absorbed out: {string.Join(", ", abs.AbsorbedOut)}.");
            }

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

            return critical.Concat(important).Concat(informational).Distinct().ToList();
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

        // ── Fisher's Exact Test ───────────────────────────────────────────────

        private static double FisherExactOneSided(int a, int b, int c, int d)
        {
            int n = a + b + c + d;
            int r1 = a + b;
            int c1 = a + c;
            double pValue = 0;
            int kMax = Math.Min(r1, c1);
            for (int k = a; k <= kMax; k++)
                pValue += HypergeometricPmf(n, c1, r1, k);
            return Math.Min(1.0, Math.Max(0.0, pValue));
        }

        private static double HypergeometricPmf(int n, int K, int draws, int k)
        {
            if (k < Math.Max(0, draws + K - n) || k > Math.Min(draws, K)) return 0;
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
