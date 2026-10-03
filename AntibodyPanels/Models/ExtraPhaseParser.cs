using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AntibodyPanels.Models
{
    public static class ExtraPhaseParser
    {
        private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        {
            "IS", "C37", "37", "37C", "AHG", "CC", "IAT"
        };

        public static readonly IReadOnlyList<string> Suggested = new[] { "RT", "PEG", "Gel", "Solid" };

        public static bool IsReserved(string? name) =>
            !string.IsNullOrWhiteSpace(name) && Reserved.Contains(name.Trim());

        public static IReadOnlyList<string> Parse(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
                return Array.Empty<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var part in configured.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (name.Length == 0 || name.Length > 12) continue;
                if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9]*$")) continue;
                if (Reserved.Contains(name)) continue;
                if (!seen.Add(name)) continue;
                list.Add(name);
                if (list.Count >= 8) break;
            }
            return list;
        }

        public static string NormalizeList(string? configured) =>
            string.Join(", ", Parse(configured));

        public static IReadOnlyList<string> CorePhases { get; } = new[] { "IS", "C37", "AHG" };

        public static IReadOnlyList<string> AllInterpretableNames(string? configured)
        {
            var list = new List<string>(CorePhases);
            list.AddRange(Parse(configured));
            return list;
        }

        public static Dictionary<string, string> Deserialize(string? json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed == null) return result;
                foreach (var (key, value) in parsed)
                {
                    if (string.IsNullOrWhiteSpace(key) || Reserved.Contains(key)) continue;
                    result[key.Trim()] = string.IsNullOrWhiteSpace(value) ? "NT" : value.Trim();
                }
            }
            catch
            {
                // Keep empty if the blob is corrupt.
            }
            return result;
        }

        public static string Serialize(IReadOnlyDictionary<string, string>? extra)
        {
            if (extra == null || extra.Count == 0) return "{}";
            var clean = extra
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !Reserved.Contains(kv.Key))
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? "NT", StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Serialize(clean);
        }
    }
}
