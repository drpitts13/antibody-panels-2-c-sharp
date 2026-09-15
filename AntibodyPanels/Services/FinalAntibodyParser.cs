using System;
using System.Collections.Generic;
using System.Linq;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Splits and joins confirmed final-ID text (anti-E; anti-K).
    /// </summary>
    public static class FinalAntibodyParser
    {
        private static readonly char[] Separators = { ';', ',' };

        public static IReadOnlyList<string> Split(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();
        }

        public static string Join(IEnumerable<string> parts) =>
            string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

        public static string Append(string? current, string antibody)
        {
            if (string.IsNullOrWhiteSpace(antibody))
                return current?.Trim() ?? string.Empty;

            var parts = Split(current).ToList();
            if (parts.Any(p => string.Equals(p, antibody, StringComparison.OrdinalIgnoreCase)))
                return Join(parts);
            parts.Add(antibody.Trim());
            return Join(parts);
        }
    }
}
