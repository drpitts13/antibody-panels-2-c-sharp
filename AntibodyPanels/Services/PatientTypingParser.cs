using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public sealed class PatientTyping
    {
        public Dictionary<string, string> Antigens { get; } = new(StringComparer.Ordinal);
        public List<string> HistoricalAntibodies { get; } = new();
        public bool PhenotypeUnreliable { get; set; }
        public string UnreliableReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses free-text phenotype / history into reviewable evidence.
    /// Does not change suspected or ruled-out lists by itself.
    /// </summary>
    public static class PatientTypingParser
    {
        private static readonly (string Code, string Profile)[] WeinerProfiles =
        {
            ("R1R1", "D+ C+ c- E- e+"),
            ("R2R2", "D+ C- c+ E+ e-"),
            ("R1R2", "D+ C+ c+ E+ e+"),
            ("R1r", "D+ C+ c+ E- e+"),
            ("R2r", "D+ C- c+ E+ e+"),
            ("R0r", "D+ C- c+ E- e+"),
            ("Ror", "D+ C- c+ E- e+"),
            ("r'r'", "D- C+ c- E- e+"),
            ("r''r''", "D- C- c+ E+ e-"),
            ("r'r", "D- C+ c+ E- e+"),
            ("r''r", "D- C- c+ E+ e+"),
            ("rr", "D- C- c+ E- e+"),
        };

        private static readonly Regex SystemPair = new(
            @"\b(Fy|Jk|Kp|Js|Lu|Le)\s*\(\s*a\s*([+\-−–])\s*(?:[,/]?\s*b\s*([+\-−–]))?\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] TransfusionHints =
        {
            "transfus", "recent rbc", "received units", "donor cells in circulation",
            "phenotype unreliable"
        };

        public static PatientTyping Parse(string? phenotype, string? previousAntibodies, string? notes)
        {
            var typing = new PatientTyping();
            ApplyWeiner(phenotype, typing);
            ApplySystemPairs(phenotype, typing);
            ApplyLooseAntigens(phenotype, typing);
            typing.HistoricalAntibodies.AddRange(ParseHistorical(previousAntibodies));
            if (LooksRecentlyTransfused(phenotype) || LooksRecentlyTransfused(notes))
            {
                typing.PhenotypeUnreliable = true;
                typing.UnreliableReason =
                    "Recent transfusion or a configured limitation was noted, so patient typing cannot be used to support or exclude an alloantibody.";
            }
            return typing;
        }

        public static List<PatientTypingConsideration> Evaluate(PatientTyping typing)
        {
            var list = new List<PatientTypingConsideration>();
            foreach (var (ag, val) in typing.Antigens.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var antibody = $"anti-{ag}";
                if (typing.PhenotypeUnreliable)
                {
                    list.Add(new PatientTypingConsideration
                    {
                        Antibody = antibody,
                        Antigen = ag,
                        Kind = PatientTypingKind.Uninterpretable,
                        PatientValue = val,
                        Explanation =
                            $"Patient types {ag}{val}, but {typing.UnreliableReason} {antibody} cannot be assessed from this phenotype.",
                    });
                    continue;
                }

                if (val == "+")
                {
                    list.Add(new PatientTypingConsideration
                    {
                        Antibody = antibody,
                        Antigen = ag,
                        Kind = PatientTypingKind.Against,
                        PatientValue = val,
                        Explanation =
                            $"Patient types {ag}+, which argues against allo{antibody} unless the typing is from donor cells or otherwise unreliable.",
                    });
                }
                else
                {
                    list.Add(new PatientTypingConsideration
                    {
                        Antibody = antibody,
                        Antigen = ag,
                        Kind = PatientTypingKind.Supporting,
                        PatientValue = val,
                        Explanation =
                            $"Patient types {ag}-, which is consistent with allo{antibody}.",
                    });
                }
            }

            foreach (var antibody in typing.HistoricalAntibodies)
            {
                list.Add(new PatientTypingConsideration
                {
                    Antibody = antibody,
                    Antigen = antibody.StartsWith("anti-", StringComparison.OrdinalIgnoreCase)
                        ? antibody[5..] : antibody,
                    Kind = PatientTypingKind.Historical,
                    Explanation = $"{antibody} was previously identified. Treat this as historical evidence, not a current identification.",
                });
            }

            return list
                .OrderBy(c => KindOrder(c.Kind))
                .ThenBy(c => c.Antibody, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool LooksRecentlyTransfused(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var lower = text.ToLowerInvariant();
            return TransfusionHints.Any(h => lower.Contains(h));
        }

        private static void ApplyWeiner(string? phenotype, PatientTyping typing)
        {
            if (string.IsNullOrWhiteSpace(phenotype)) return;
            var compact = phenotype.Replace(" ", "", StringComparison.Ordinal);
            string? bestCode = null;
            string? bestProfile = null;
            foreach (var (code, profile) in WeinerProfiles)
            {
                if (!ContainsWeiner(compact, code)) continue;
                if (bestCode == null || code.Length > bestCode.Length)
                {
                    bestCode = code;
                    bestProfile = profile;
                }
            }
            if (bestProfile != null)
                ApplyLooseAntigens(bestProfile, typing);
        }

        private static bool ContainsWeiner(string compact, string code)
        {
            var idx = compact.IndexOf(code, StringComparison.OrdinalIgnoreCase);
            return idx >= 0;
        }

        private static void ApplySystemPairs(string? phenotype, PatientTyping typing)
        {
            if (string.IsNullOrWhiteSpace(phenotype)) return;
            foreach (Match m in SystemPair.Matches(phenotype))
            {
                var prefix = m.Groups[1].Value.ToLowerInvariant() switch
                {
                    "fy" => "Fy",
                    "jk" => "Jk",
                    "kp" => "Kp",
                    "js" => "Js",
                    "lu" => "Lu",
                    "le" => "Le",
                    _ => m.Groups[1].Value
                };
                SetIfKnown(typing, prefix + "a", NormalizeSign(m.Groups[2].Value));
                if (m.Groups[3].Success)
                    SetIfKnown(typing, prefix + "b", NormalizeSign(m.Groups[3].Value));
            }
        }

        private static void ApplyLooseAntigens(string? phenotype, PatientTyping typing)
        {
            if (string.IsNullOrWhiteSpace(phenotype)) return;
            var names = AntigenConstants.AllKnownAntigens
                .OrderByDescending(n => n.Length)
                .ToList();
            foreach (var name in names)
            {
                var pattern = $@"(?<![A-Za-z]){Regex.Escape(name)}\s*([+\-−–])";
                var match = Regex.Match(phenotype, pattern);
                if (!match.Success) continue;
                // Keep E/e C/c K/k S/s case-sensitive.
                if (name is "E" or "e" or "C" or "c" or "K" or "k" or "S" or "s")
                {
                    var idx = phenotype.IndexOf(name + match.Groups[1].Value, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        var spaced = Regex.Match(phenotype, $@"(?<![A-Za-z]){Regex.Escape(name)}\s+([+\-−–])");
                        if (!spaced.Success) continue;
                        SetIfKnown(typing, name, NormalizeSign(spaced.Groups[1].Value));
                        continue;
                    }
                }
                SetIfKnown(typing, name, NormalizeSign(match.Groups[1].Value));
            }
        }

        private static IEnumerable<string> ParseHistorical(string? previous)
        {
            if (string.IsNullOrWhiteSpace(previous)) yield break;
            var parts = previous.Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in parts)
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                token = token.Replace("allo", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (token.StartsWith("anti-", StringComparison.OrdinalIgnoreCase))
                    token = "anti-" + token[5..].Trim();
                else if (AntigenConstants.IsKnown(token))
                    token = "anti-" + token;
                else
                    continue;
                if (seen.Add(token))
                    yield return token;
            }
        }

        private static void SetIfKnown(PatientTyping typing, string antigen, string value)
        {
            var resolved = AntigenConstants.AllKnownAntigens
                .FirstOrDefault(a => string.Equals(a, antigen, StringComparison.Ordinal));
            if (resolved == null)
            {
                resolved = AntigenConstants.AllKnownAntigens
                    .FirstOrDefault(a => string.Equals(a, antigen, StringComparison.OrdinalIgnoreCase));
            }
            if (resolved == null) return;
            typing.Antigens[resolved] = value;
        }

        private static string NormalizeSign(string raw)
        {
            var c = raw.Trim();
            return c is "-" or "−" or "–" ? "-" : "+";
        }

        private static int KindOrder(PatientTypingKind kind) => kind switch
        {
            PatientTypingKind.Against => 0,
            PatientTypingKind.Uninterpretable => 1,
            PatientTypingKind.Historical => 2,
            _ => 3
        };
    }
}
