using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Resolves the lab's configured rule-out policy and formats reviewable
    /// explanations. This is decision support: the sentence states why a
    /// criterion was or was not met; it is not a clinical diagnosis.
    /// </summary>
    public static class RuleoutPolicy
    {
        public const int MinAllowedCount = 1;
        public const int MaxAllowedCount = 5;
        public const int DefaultLabCount = 1;

        public static int ClampRequiredCount(int count)
        {
            if (count < MinAllowedCount) return MinAllowedCount;
            if (count > MaxAllowedCount) return MaxAllowedCount;
            return count;
        }

        public static Rule? FindAntibodyRule(string antibody, IEnumerable<Rule> rules)
        {
            foreach (var rule in rules)
            {
                if (string.Equals(rule.Antibody, antibody, StringComparison.OrdinalIgnoreCase))
                    return rule;
            }
            return null;
        }

        public static int ResolveRequiredCount(string antibody, IEnumerable<Rule> rules, int labDefault)
        {
            var rule = FindAntibodyRule(antibody, rules);
            return ClampRequiredCount(rule?.MinRuleoutCount ?? labDefault);
        }

        public static bool HeterozygousAllowed(string antigen, IEnumerable<Rule> rules)
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

        public static string DisplayAntibody(string antibody)
        {
            if (string.IsNullOrWhiteSpace(antibody)) return antibody;
            if (antibody.StartsWith("anti-", StringComparison.OrdinalIgnoreCase))
                return "Anti-" + antibody[5..];
            return antibody;
        }

        public static string FormatCellList(IEnumerable<RuleoutDetail> cells)
        {
            var numbers = cells
                .Select(c => c.CellNumber)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            if (numbers.Count == 0) return string.Empty;
            if (numbers.Count == 1) return $"(cell {numbers[0]})";
            return $"(cells {string.Join(", ", numbers)})";
        }

        public static string FormatExplanation(RuleoutEvaluation evaluation)
        {
            var name = DisplayAntibody(evaluation.Antibody);
            var het = evaluation.HeterozygousAllowed
                ? "heterozygous cells allowed"
                : "heterozygous cells not allowed";
            var cellRef = FormatCellList(evaluation.Cells);
            var cellSuffix = string.IsNullOrEmpty(cellRef) ? "" : $" {cellRef}";

            if (evaluation.MeetsCriteria)
            {
                var observed = evaluation.ObservedCount == 1
                    ? "1 antigen-positive nonreactive cell was observed"
                    : $"{evaluation.ObservedCount} antigen-positive nonreactive cells were observed";
                return $"{name} currently meets configured rule-out criteria because {observed}{cellSuffix} (required {evaluation.RequiredCount}; {het}).";
            }

            var zygosityWord = evaluation.HeterozygousAllowed
                ? "antigen-positive"
                : "homozygous antigen-positive";
            return $"{name} does not yet meet configured rule-out criteria ({evaluation.ObservedCount} of {evaluation.RequiredCount} {zygosityWord} nonreactive cells).";
        }
    }
}
