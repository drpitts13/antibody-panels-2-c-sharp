using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Captures which rule-out policy produced an analysis. Evidence for
    /// reviewers, not a diagnosis.
    /// </summary>
    public static class AnalysisRuleTrace
    {
        public const string EngineVersion = "1";

        public static string Serialize(IEnumerable<Rule> rules, LabSettings settings)
        {
            var payload = new
            {
                RuleEngineVersion = EngineVersion,
                settings.DefaultMinRuleoutCount,
                settings.AcsRuleoutCount,
                settings.IdentificationCellCount,
                Rules = (rules ?? Array.Empty<Rule>())
                    .OrderBy(r => r.Antibody, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new
                    {
                        r.Antibody,
                        r.MinRuleoutCount,
                        r.HeterozygousOk,
                        r.ExceptionAntigen
                    })
                    .ToList()
            };
            return JsonSerializer.Serialize(payload);
        }

        public static string Explain(IEnumerable<Rule> rules, LabSettings settings)
        {
            var list = (rules ?? Array.Empty<Rule>()).ToList();
            var sb = new StringBuilder();
            sb.Append("Rule engine v").Append(EngineVersion)
                .Append(". Lab default rule-out count is ")
                .Append(settings.DefaultMinRuleoutCount)
                .Append("; ACS requires ")
                .Append(settings.AcsRuleoutCount)
                .Append(" observed qualifying cells; identification rule is ")
                .Append(settings.IdentificationRuleLabel)
                .Append('.');
            if (list.Count == 0)
            {
                sb.Append(" No per-antibody rule overrides were configured.");
            }
            else
            {
                sb.Append(" Per-antibody overrides: ");
                var parts = list
                    .OrderBy(r => r.Antibody, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .Select(FormatRule)
                    .ToList();
                sb.Append(string.Join("; ", parts)).Append('.');
                if (list.Count > 8)
                    sb.Append(" Additional antibody rules were omitted from this summary.");
            }
            sb.Append(" This is the policy used for this analysis, not a diagnosis.");
            return sb.ToString();
        }

        public static string ResolveOperator(LabSettings? settings = null)
        {
            var initials = LabSettings.NormalizeInitials(settings?.DefaultIdentifiedBy);
            if (!string.IsNullOrWhiteSpace(initials))
                return initials;
            var windows = Environment.UserName?.Trim() ?? "";
            return windows.Length > 64 ? windows[..64] : windows;
        }

        public static string FormatOperatorNote(LabSettings? settings = null)
        {
            var initials = LabSettings.NormalizeInitials(settings?.DefaultIdentifiedBy);
            var windows = (Environment.UserName ?? "").Trim();
            if (windows.Length > 64) windows = windows[..64];
            if (!string.IsNullOrWhiteSpace(initials) && !string.IsNullOrWhiteSpace(windows) &&
                !string.Equals(initials, windows, StringComparison.OrdinalIgnoreCase))
            {
                return $"Analysis recorded for operator {initials} (Windows login {windows}). " +
                       "This identifies who ran the analysis, not a diagnosis.";
            }
            if (!string.IsNullOrWhiteSpace(initials))
            {
                return $"Analysis recorded for operator {initials}. " +
                       "This identifies who ran the analysis, not a diagnosis.";
            }
            if (!string.IsNullOrWhiteSpace(windows))
            {
                return $"Analysis recorded for Windows login {windows}. Lab initials were not set. " +
                       "This identifies who ran the analysis, not a diagnosis.";
            }
            return "Operator was not recorded for this analysis. Not a diagnosis.";
        }

        private static string FormatRule(Rule rule)
        {
            var name = string.IsNullOrWhiteSpace(rule.Antibody) ? rule.Name : rule.Antibody;
            var text = $"{name} requires {rule.MinRuleoutCount}";
            if (rule.HeterozygousOk)
            {
                var ag = string.IsNullOrWhiteSpace(rule.ExceptionAntigen)
                    ? "heterozygous cells"
                    : $"heterozygous {rule.ExceptionAntigen}";
                text += $" ({ag} allowed)";
            }
            return text;
        }
    }
}
