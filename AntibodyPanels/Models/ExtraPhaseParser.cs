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

        public static readonly IReadOnlyList<string> TiterGridNames =
            new[] { "Dil1", "Dil2", "Dil4", "Dil8", "Dil16", "Dil32", "Dil64", "Dil128" };

        private static readonly Regex DilutionName = new(
            @"^(?:(?:Dil(?:ution|n)?|Titer)\s*)?(\d{1,5})$|^1\s*[:/]\s*(\d{1,5})$|^(Neat|Undiluted)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> IatLike = new(StringComparer.OrdinalIgnoreCase)
        {
            "AHG", "IAT", "Gel", "Solid", "PEG"
        };

        public static bool IsReserved(string? name) =>
            !string.IsNullOrWhiteSpace(name) && Reserved.Contains(name.Trim());

        public static bool IsIatLike(string? name) =>
            !string.IsNullOrWhiteSpace(name) && IatLike.Contains(name.Trim()) && !IsDilution(name);

        public static bool IsDilution(string? name) => TryParseDilution(name, out _);

        /// <summary>
        /// Serial-dilution column (Dil4, 1:16, Neat). Not an IAT identification phase.
        /// </summary>
        public static bool TryParseDilution(string? name, out int dilution)
        {
            dilution = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            if (trimmed.Equals("Dil", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Diln", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Dilution", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Titer", StringComparison.OrdinalIgnoreCase))
                return false;
            var m = DilutionName.Match(trimmed);
            if (!m.Success) return false;
            if (m.Groups[3].Success)
            {
                dilution = 1;
                return true;
            }
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!int.TryParse(raw, out var n) || n < 1 || n > 1024) return false;
            if ((n & (n - 1)) != 0) return false;
            dilution = n;
            return true;
        }

        public static bool Contains(string? configured, string name) =>
            Parse(configured).Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

        public static string CanonicalName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            return Suggested.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase))
                   ?? trimmed;
        }

        /// <summary>
        /// Builds the lab ExtraPhases list from suggested method columns plus any
        /// custom names already stored. Empty stays empty — tube-only labs keep
        /// the classic IS / 37°C / AHG / CC grid.
        /// </summary>
        public static string FromSuggested(bool rt, bool peg, bool gel, bool solid, string? current = null)
        {
            var custom = Parse(current)
                .Where(n => !Suggested.Any(s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var list = new List<string>();
            if (rt) list.Add("RT");
            if (peg) list.Add("PEG");
            if (gel) list.Add("Gel");
            if (solid) list.Add("Solid");
            list.AddRange(custom);
            return string.Join(", ", list);
        }

        public static string Toggle(string? configured, string name, bool enabled)
        {
            if (IsReserved(name) || !IsValidName(name))
                return NormalizeList(configured);
            var canonical = CanonicalName(name);
            var list = Parse(configured).ToList();
            var idx = list.FindIndex(p => string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase));
            if (enabled && idx < 0)
                list.Add(canonical);
            else if (!enabled && idx >= 0)
                list.RemoveAt(idx);
            return string.Join(", ", list);
        }

        public static IReadOnlyList<string> Parse(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
                return Array.Empty<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var part in configured.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim();
                if (!IsValidName(name)) continue;
                if (Reserved.Contains(name)) continue;
                if (!seen.Add(name)) continue;
                list.Add(name);
                if (list.Count >= 12) break;
            }
            return list;
        }

        private static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            if (trimmed.Length is < 1 or > 12) return false;
            if (Regex.IsMatch(trimmed, @"^[A-Za-z][A-Za-z0-9]*$")) return true;
            return IsDilution(trimmed) && Regex.IsMatch(trimmed, @"^1\s*[:/]\s*\d{1,4}$");
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
