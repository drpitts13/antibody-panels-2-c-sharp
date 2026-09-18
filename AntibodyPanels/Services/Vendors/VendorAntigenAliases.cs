using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services.Vendors
{
    public static class VendorAntigenAliases
    {
        private static readonly Dictionary<string, string> Map =
            new(StringComparer.OrdinalIgnoreCase);

        static VendorAntigenAliases()
        {
            foreach (var ag in AntigenConstants.AllKnownAntigens)
            {
                foreach (var key in Expand(ag))
                    Map.TryAdd(key, ag);
            }

            Add("CW", "Cw");
            Add("C^W", "Cw");
            Add("LU^A", "Lua");
            Add("LU^B", "Lub");
            Add("KP^A", "Kpa");
            Add("KP^B", "Kpb");
            Add("JS^A", "Jsa");
            Add("JS^B", "Jsb");
            Add("JK^A", "Jka");
            Add("JK^B", "Jkb");
            Add("FY^A", "Fya");
            Add("FY^B", "Fyb");
            Add("LE^A", "Lea");
            Add("LE^B", "Leb");
            Add("XG^A", "Xga");
            Add("P1", "P1");
            Add("P", "P1");
            Add("DO^A", "Doa");
            Add("DO^B", "Dob");
            Add("DI^A", "Dia");
            Add("DI^B", "Dib");
            Add("WR^A", "Wra");
            Add("WR^B", "Wrb");
            Add("CO^A", "Coa");
            Add("CO^B", "Cob");
            Add("YT^A", "Yta");
            Add("YT^B", "Ytb");
            Add("LW^A", "LWa");
            Add("LW^B", "LWb");
            Add("MI^A", "Mia");
            Add("GO^A", "Goa");
            Add("JR^A", "Jra");
            Add("CR^A", "Cra");
            Add("KN^A", "Kna");
            Add("MCCA", "McCa");
            Add("YK^A", "Yka");
            Add("JO^A", "Joa");
            Add("IN^B", "Inb");
            Add("AT^A", "Ata");
            Add("GE2", "Ge2");
            Add("GE3", "Ge3");
            Add("SC1", "Sc1");
            Add("SC2", "Sc2");
        }

        private static void Add(string alias, string antigen)
        {
            if (AntigenConstants.IsKnown(antigen))
                Map.TryAdd(NormalizeKey(alias), antigen);
        }

        private static IEnumerable<string> Expand(string antigen)
        {
            yield return NormalizeKey(antigen);
            if (antigen.Length >= 3 && char.IsLetter(antigen[^1]))
            {
                var stem = antigen[..^1];
                var last = antigen[^1];
                yield return NormalizeKey(stem + "^" + last);
                yield return NormalizeKey(stem + last.ToString().ToLowerInvariant());
            }
        }

        public static string? Resolve(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var trimmed = raw.Trim();
            if (trimmed.Length == 1 && AntigenConstants.IsKnown(trimmed))
                return trimmed;
            var key = NormalizeKey(raw);
            return Map.TryGetValue(key, out var ag) ? ag : null;
        }

        public static string NormalizeKey(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw.Normalize(NormalizationForm.FormKD))
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch) || ch == '^')
                    sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns +, -, or null when the value should stay untyped (NT / blank extra).
        /// </summary>
        public static string? NormalizeValue(string? raw, bool required)
        {
            var v = (raw ?? string.Empty).Trim();
            if (v.Length == 0)
                return required ? "-" : null;
            var compact = v.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
            if (compact is "NT" or "N.T." or "N/T" or "?" or "ND")
                return required ? "-" : null;
            if (compact is "+" or "POS" or "POSITIVE" or "1" or "TRUE" or "+W" or "W+"
                or "+S" or "S+" or "W" or "S" or "(+)" or "++")
                return "+";
            if (compact is "-" or "−" or "–" or "0" or "NEG" or "NEGATIVE" or "FALSE" or "Ø")
                return "-";
            return null;
        }
    }
}
