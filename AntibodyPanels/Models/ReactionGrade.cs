using System;
using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    /// <summary>
    /// Canonical reaction grades and how they count as evidence.
    /// w+, MF, and hemolysis are reactive: they cannot support a rule-out.
    /// </summary>
    public static class ReactionGrade
    {
        public const string Negative = "0";
        public const string Weak = "w+";
        public const string One = "1+";
        public const string Two = "2+";
        public const string Three = "3+";
        public const string Four = "4+";
        public const string MixedField = "MF";
        public const string Hemolysis = "H";
        public const string NotTested = "NT";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Negative, Weak, One, Two, Three, Four, MixedField, Hemolysis, NotTested
        };

        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return NotTested;
            var v = raw.Trim();
            if (v.Equals(Negative, StringComparison.OrdinalIgnoreCase)) return Negative;
            if (v is "w+" or "W+" or "w" or "W" or "wk" or "WK" or "weak" or "Weak" or "WEAK")
                return Weak;
            if (v is "1+" or "1") return One;
            if (v is "2+" or "2") return Two;
            if (v is "3+" or "3") return Three;
            if (v is "4+" or "4") return Four;
            if (v is "MF" or "mf" or "Mf" or "mixed" or "Mixed" or "MIXED" or "mixed field" or "mixed-field")
                return MixedField;
            if (v is "H" or "h" or "H+" or "HL" or "hl" or "hemolysis" or "Hemolysis" or "HEMOLYSIS"
                or "hemolyzed" or "Haemolysis")
                return Hemolysis;
            if (v is "NT" or "nt" or "N.T." or "N/T") return NotTested;
            return v;
        }

        public static bool IsNotTested(string? raw)
        {
            var v = Normalize(raw);
            return v == NotTested;
        }

        public static bool IsNegative(string? raw) => Normalize(raw) == Negative;

        public static bool IsAbsent(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var v = Normalize(raw);
            return v == NotTested || v == Negative;
        }

        public static bool IsPositive(string? raw) => !IsAbsent(raw);

        public static bool IsSpecial(string? raw)
        {
            var v = Normalize(raw);
            return v == MixedField || v == Hemolysis;
        }

        public static double Strength(string? raw)
        {
            return Normalize(raw) switch
            {
                Negative or NotTested => 0,
                Weak => 0.5,
                One => 1,
                Two => 2,
                Three => 3,
                Four => 4,
                MixedField => 1,
                Hemolysis => 4,
                _ => 0
            };
        }

        public static string Describe(string? raw)
        {
            return Normalize(raw) switch
            {
                Weak => "weak (w+)",
                MixedField => "mixed-field (MF)",
                Hemolysis => "hemolysis (H)",
                One => "1+",
                Two => "2+",
                Three => "3+",
                Four => "4+",
                Negative => "0",
                NotTested => "NT",
                var other => other
            };
        }
    }
}
