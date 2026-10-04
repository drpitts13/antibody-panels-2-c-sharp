using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Assembles a reviewable per-candidate narrative from analysis evidence.
    /// This is decision support, not a diagnosis.
    /// </summary>
    public static class AnalysisExplainer
    {
        public static List<CandidateExplanation> Build(AnalysisResult result)
        {
            var antibodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ab in result.Suspected.Keys) antibodies.Add(ab);
            foreach (var ev in result.RuleoutEvaluations) antibodies.Add(ev.Antibody);
            foreach (var pm in result.PatternMatches.Take(8)) antibodies.Add(pm.Antibody);
            foreach (var u in result.UntypeableSpecificities) antibodies.Add(u.Antibody);
            foreach (var c in result.PatientTypingConsiderations.Where(x =>
                         x.Kind == PatientTypingKind.Historical))
                antibodies.Add(c.Antibody);

            var list = antibodies
                .Select(ab => Explain(ab, result))
                .OrderBy(e => StatusOrder(e.Status))
                .ThenBy(e => e.Antibody, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list;
        }

        public static string FormatDocument(AnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Explain Analysis — {result.SpecimenId}");
            sb.AppendLine("Decision support for a qualified professional. Not a diagnosis.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(result.OperatorNote))
            {
                sb.AppendLine(result.OperatorNote);
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(result.RuleConfigurationNote))
            {
                sb.AppendLine(result.RuleConfigurationNote);
                sb.AppendLine();
            }
            if (result.SpecialReactionNotes.Count > 0)
            {
                sb.AppendLine("Special reaction grades:");
                foreach (var note in result.SpecialReactionNotes)
                    sb.AppendLine("  " + note);
                sb.AppendLine();
            }
            if (result.ReactionPatterns.Count > 0)
            {
                sb.AppendLine("Reaction patterns (evidence only):");
                foreach (var pattern in result.ReactionPatterns)
                    sb.AppendLine("  " + pattern.Explanation);
                sb.AppendLine();
            }
            if (result.UntypeableSpecificities.Count > 0)
            {
                sb.AppendLine("Specificities the current panels cannot type (antigen never imported):");
                foreach (var u in result.UntypeableSpecificities)
                    sb.AppendLine("  " + u.Explanation);
                sb.AppendLine();
            }
            foreach (var exp in result.CandidateExplanations.Count > 0
                         ? result.CandidateExplanations
                         : Build(result))
            {
                if (exp.Status.Contains("Cannot type", StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.AppendLine(exp.Narrative);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        public static CandidateExplanation Explain(string antibody, AnalysisResult result)
        {
            var exp = new CandidateExplanation { Antibody = antibody };
            result.Suspected.TryGetValue(antibody, out var score);
            var suspected = result.Suspected.ContainsKey(antibody);
            var ruledOut = result.RuledOut.ContainsKey(antibody);
            var ruleEval = result.RuleoutEvaluations
                .FirstOrDefault(e => string.Equals(e.Antibody, antibody, StringComparison.OrdinalIgnoreCase));
            result.SuspectedEvidence.TryGetValue(antibody, out var evidence);
            var dosage = result.DosageEffects
                .FirstOrDefault(d => string.Equals(d.Antibody, antibody, StringComparison.OrdinalIgnoreCase));
            var phenotype = result.PatientTypingConsiderations
                .Where(c => string.Equals(c.Antibody, antibody, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var pattern = result.PatternMatches
                .FirstOrDefault(p => string.Equals(p.Antibody, antibody, StringComparison.OrdinalIgnoreCase));

            var untypeable = result.UntypeableSpecificities.FirstOrDefault(u =>
                string.Equals(u.Antibody, antibody, StringComparison.OrdinalIgnoreCase));
            if (untypeable != null)
                return ExplainUntypeable(antibody, untypeable, phenotype);

            exp.Status = ResolveStatus(suspected, ruledOut, ruleEval, phenotype, pattern);
            if (ruleEval != null)
            {
                exp.RuleoutCriteria = ruleEval.Explanation;
                exp.RuleoutCells = ruleEval.Cells
                    .Select(c => $"{c.PanelName} cell {c.CellNumber} ({(c.IsHomozygous ? "homozygous" : "heterozygous")})")
                    .ToList();
            }
            else
            {
                exp.RuleoutCriteria = ruledOut
                    ? $"{antibody} is listed as ruled out ({result.RuledOut[antibody]} qualifying cells)."
                    : $"{antibody} does not yet have configured rule-out evidence.";
            }

            if (evidence != null)
            {
                exp.SupportingCells = evidence.SupportingCells
                    .Select(c => $"{c.PanelName} cell {c.CellNumber} ({c.StrongestPhase} {c.StrongestValue})")
                    .ToList();
                exp.ConflictingCells = evidence.ConflictingCells
                    .Select(c => $"{c.PanelName} cell {c.CellNumber} ({c.StrongestPhase} {c.StrongestValue})")
                    .ToList();
            }

            if (ruleEval is { ConflictingReactiveCells.Count: > 0 })
            {
                foreach (var cell in ruleEval.ConflictingReactiveCells)
                {
                    var line = $"cell {cell} (antigen-positive and reactive)";
                    if (!exp.ConflictingCells.Any(x => x.Contains($"cell {cell}", StringComparison.Ordinal)))
                        exp.ConflictingCells.Add(line);
                }
            }

            if (dosage != null)
            {
                exp.DosageNote =
                    $"Dosage: homozygous average {dosage.AvgHomozygous:F2} vs heterozygous {dosage.AvgHeterozygous:F2} ({dosage.Severity}).";
            }

            exp.PhaseNote = FormatPhaseNote(antibody, result);
            exp.PhenotypeNote = phenotype.Count == 0
                ? "No patient phenotype evidence was parsed for this antibody."
                : string.Join(" ", phenotype.Select(p => p.Explanation));

            var extra = result.SelectedCellRecommendations
                .Where(r => r.Explanation.Contains(antibody, StringComparison.OrdinalIgnoreCase) ||
                            r.Distinguishes.Any(d => d.Contains(antibody, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.Explanation)
                .ToList();
            exp.AdditionalTesting = extra.Count > 0
                ? string.Join(" ", extra)
                : "No selected-cell recommendation currently names this antibody.";

            if (result.SuspectedStatistics.TryGetValue(antibody, out var stats))
                exp.IdentificationNote = stats.IdentificationDetail;

            exp.Narrative = FormatNarrative(exp, score, suspected);
            return exp;
        }

        private static CandidateExplanation ExplainUntypeable(
            string antibody,
            UntypeableSpecificity untypeable,
            List<PatientTypingConsideration> phenotype)
        {
            var historical = phenotype.Any(p => p.Kind == PatientTypingKind.Historical);
            var exp = new CandidateExplanation
            {
                Antibody = antibody,
                Status = historical
                    ? "Historical — cannot type from current panels"
                    : "Cannot type from current panels",
                RuleoutCriteria = untypeable.Explanation,
                PhaseNote = "Phase scores were not computed because the antigen was never imported.",
                PhenotypeNote = phenotype.Count == 0
                    ? "No patient phenotype evidence was parsed for this antibody."
                    : string.Join(" ", phenotype.Select(p => p.Explanation)),
                AdditionalTesting =
                    $"Import or select cells that type {untypeable.Antigen} before this antibody can be ruled out or supported.",
            };
            exp.Narrative = FormatNarrative(exp, score: 0, suspected: false);
            return exp;
        }

        private static string ResolveStatus(bool suspected, bool ruledOut, RuleoutEvaluation? ruleEval,
            List<PatientTypingConsideration> phenotype, PatternMatch? pattern)
        {
            if (suspected && ruledOut) return "Suspected and ruled out — review";
            if (suspected) return "Suspected";
            if (ruleEval?.MeetsCriteria == true || ruledOut) return "Ruled out";
            if (ruleEval != null) return "Rule-out in progress";
            if (phenotype.Any(p => p.Kind == PatientTypingKind.Historical)) return "Historical";
            if (pattern != null) return "Pattern only";
            return "Considered";
        }

        private static string FormatPhaseNote(string antibody, AnalysisResult result)
        {
            if (result.PhraseProbabilities.Count == 0) return "Phase-specific scores were not available.";
            var parts = new List<(string Phase, double Score)>();
            foreach (var (phase, scores) in result.PhraseProbabilities)
            {
                if (scores.TryGetValue(antibody, out var score) && score > 0)
                    parts.Add((phase, score));
            }
            if (parts.Count == 0)
                return "No phase-specific score favored this antibody.";
            var best = parts.OrderByDescending(p => p.Score).First();
            var label = best.Phase switch
            {
                "C37" => "37°C",
                "IS" => "immediate spin",
                _ => best.Phase
            };
            return $"Phase evidence is strongest at {label}.";
        }

        private static string FormatNarrative(CandidateExplanation exp, double score, bool suspected)
        {
            var sb = new StringBuilder();
            sb.Append(exp.Antibody).Append(" — ").Append(exp.Status).Append('.');
            if (suspected)
                sb.Append(" Combined analytical score ").Append((score * 100).ToString("F1")).Append("%.");
            if (!string.IsNullOrWhiteSpace(exp.IdentificationNote))
                sb.Append(' ').Append(exp.IdentificationNote).Append('.');
            sb.Append(' ').Append(exp.RuleoutCriteria);
            if (exp.RuleoutCells.Count > 0)
                sb.Append(" Cells used for rule-out: ").Append(string.Join("; ", exp.RuleoutCells)).Append('.');
            if (exp.SupportingCells.Count > 0)
                sb.Append(" Supporting evidence: ").Append(exp.SupportingCells.Count)
                    .Append(" reactive antigen-positive cell(s) (")
                    .Append(string.Join("; ", exp.SupportingCells)).Append(").");
            else if (exp.Status is "Suspected" or "Suspected and ruled out — review")
                sb.Append(" No supporting antigen-positive reactive cells were listed.");
            if (exp.ConflictingCells.Count > 0)
                sb.Append(" Cells that block rule-out: ").Append(string.Join("; ", exp.ConflictingCells)).Append('.');
            if (!string.IsNullOrWhiteSpace(exp.DosageNote))
                sb.Append(' ').Append(exp.DosageNote);
            if (!string.IsNullOrWhiteSpace(exp.PhaseNote))
                sb.Append(' ').Append(exp.PhaseNote);
            sb.Append(' ').Append(exp.PhenotypeNote);
            sb.Append(" Additional useful testing: ").Append(exp.AdditionalTesting);
            return sb.ToString();
        }

        private static int StatusOrder(string status) => status switch
        {
            "Suspected and ruled out — review" => 0,
            "Suspected" => 1,
            "Rule-out in progress" => 2,
            "Cannot type from current panels" => 3,
            "Historical — cannot type from current panels" => 3,
            "Historical" => 4,
            "Pattern only" => 5,
            "Ruled out" => 6,
            _ => 7
        };
    }
}
