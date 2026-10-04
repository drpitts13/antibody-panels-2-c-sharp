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
        public Dictionary<string, string> PredictedAntigens { get; } = new(StringComparer.Ordinal);
        public List<string> HistoricalAntibodies { get; } = new();
        public List<AlleleVariantNote> VariantNotes { get; } = new();
        public bool PhenotypeUnreliable { get; set; }
        public string UnreliableReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses free-text phenotype / genotype / history into reviewable evidence.
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

        private static readonly Regex AlleleToken = new(
            @"\b(RHCE|RHD|FY|JK|KEL|GYPA|GYPB)\s*\*\s*([A-Za-z0-9.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AllelePair = new(
            @"\b(RHCE|RHD|FY|JK|KEL|GYPA|GYPB)\s*\*\s*([A-Za-z0-9.]+)\s*/\s*(?:\1\s*\*\s*)?([A-Za-z0-9.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] TransfusionHints =
        {
            "transfus", "recent rbc", "received units", "donor cells in circulation",
            "phenotype unreliable"
        };

        public static PatientTyping Parse(string? phenotype, string? previousAntibodies, string? notes,
            string? genotype = null)
        {
            var typing = new PatientTyping();
            ApplyWeiner(phenotype, typing.Antigens);
            ApplySystemPairs(phenotype, typing.Antigens);
            ApplyLooseAntigens(phenotype, typing.Antigens);
            ApplyGenotype(genotype, typing.PredictedAntigens);
            typing.VariantNotes.AddRange(AlleleVariantCatalog.Match(genotype));
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

            var suppressSupport = new HashSet<string>(
                typing.VariantNotes.Where(v => v.SuppressPredictedSupport).Select(v => v.Antigen),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (ag, val) in typing.PredictedAntigens.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                typing.Antigens.TryGetValue(ag, out var serology);
                if (serology == val && !typing.PhenotypeUnreliable)
                    continue;
                if (val == "-" && suppressSupport.Contains(ag))
                    continue;

                var antibody = $"anti-{ag}";
                var conflict = serology != null && serology != val;
                string explanation;
                if (conflict && typing.PhenotypeUnreliable)
                {
                    explanation =
                        $"Serologic typing is {ag}{serology}, but that phenotype is uninterpretable after transfusion or a configured limitation. " +
                        $"The recorded genotype predicts {ag}{val}. Use the molecular prediction as reviewable evidence for allo{antibody}, not a diagnosis.";
                }
                else if (conflict)
                {
                    explanation =
                        $"Serologic typing is {ag}{serology} but the recorded genotype predicts {ag}{val}. " +
                        "Review for mistype, variant antigen, or donor cells. This is evidence, not a diagnosis.";
                }
                else if (val == "+")
                {
                    explanation =
                        $"The recorded genotype predicts {ag}+. Molecular predictions remain usable after transfusion and argue against allo{antibody} unless a variant or incomplete genotype is present.";
                }
                else
                {
                    explanation =
                        $"The recorded genotype predicts {ag}-, which is consistent with allo{antibody}. This is predicted phenotype evidence, not a serologic type or a diagnosis.";
                }

                list.Add(new PatientTypingConsideration
                {
                    Antibody = antibody,
                    Antigen = ag,
                    Kind = PatientTypingKind.Predicted,
                    PatientValue = val,
                    Explanation = explanation,
                });
            }

            foreach (var variant in typing.VariantNotes)
            {
                list.Add(new PatientTypingConsideration
                {
                    Antibody = variant.Antibody,
                    Antigen = variant.Antigen,
                    Kind = PatientTypingKind.Variant,
                    Explanation = variant.Explanation,
                });
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

        private static void ApplyGenotype(string? genotype, Dictionary<string, string> predicted)
        {
            if (string.IsNullOrWhiteSpace(genotype)) return;
            ApplyWeiner(genotype, predicted);
            ApplySystemPairs(genotype, predicted);
            ApplyLooseAntigens(genotype, predicted);
            ApplyAlleles(genotype, predicted);
        }

        private static void ApplyAlleles(string? genotype, Dictionary<string, string> predicted)
        {
            if (string.IsNullOrWhiteSpace(genotype)) return;
            var rhce = new List<string>();
            var rhdExpress = new List<bool>();
            var fy = new List<string>();
            var jk = new List<string>();
            var kel = new List<string>();
            var gypa = new List<string>();
            var gypb = new List<string>();

            void AddAllele(string system, string allele)
            {
                switch (system.ToUpperInvariant())
                {
                    case "RHCE":
                        var hap = NormalizeRhce(allele);
                        if (hap != null) rhce.Add(hap);
                        break;
                    case "RHD":
                        if (AlleleVariantCatalog.IsRhdNull(allele))
                            rhdExpress.Add(false);
                        else if (AlleleVariantCatalog.IsRhdPartialOrWeak(allele) &&
                                 NormalizeKey(allele).Contains("EL"))
                            break;
                        else if (AlleleVariantCatalog.IsRhdPartialOrWeak(allele) &&
                                 (NormalizeKey(allele).Contains("01W") ||
                                  NormalizeKey(allele).Contains("WEAK")))
                            rhdExpress.Add(true);
                        else if (!AlleleVariantCatalog.IsRhdPartialOrWeak(allele))
                            rhdExpress.Add(true);
                        break;
                    case "FY":
                        var fyCode = NormalizeFyJk(allele, "A", "B");
                        if (fyCode != null) fy.Add(fyCode);
                        break;
                    case "JK":
                        var jkCode = NormalizeFyJk(allele, "A", "B");
                        if (jkCode != null) jk.Add(jkCode);
                        break;
                    case "KEL":
                        var kelCode = NormalizeKel(allele);
                        if (kelCode != null) kel.Add(kelCode);
                        break;
                    case "GYPA":
                        var mnsMn = NormalizeGypa(allele);
                        if (mnsMn != null) gypa.Add(mnsMn);
                        break;
                    case "GYPB":
                        var mnsSs = NormalizeGypb(allele);
                        if (mnsSs != null) gypb.Add(mnsSs);
                        break;
                }
            }

            foreach (Match m in AllelePair.Matches(genotype))
            {
                AddAllele(m.Groups[1].Value, m.Groups[2].Value);
                AddAllele(m.Groups[1].Value, m.Groups[3].Value);
            }

            foreach (Match m in AlleleToken.Matches(genotype))
                AddAllele(m.Groups[1].Value, m.Groups[2].Value);

            if (rhce.Count >= 2)
            {
                SetIfAbsent(predicted, "C", rhce.Any(h => h[0] == 'C') ? "+" : "-");
                SetIfAbsent(predicted, "c", rhce.Any(h => h[0] == 'c') ? "+" : "-");
                SetIfAbsent(predicted, "E", rhce.Any(h => h[1] == 'E') ? "+" : "-");
                SetIfAbsent(predicted, "e", rhce.Any(h => h[1] == 'e') ? "+" : "-");
            }
            else if (rhce.Count == 1)
            {
                var h = rhce[0];
                if (h[0] == 'C') SetIfAbsent(predicted, "C", "+");
                if (h[0] == 'c') SetIfAbsent(predicted, "c", "+");
                if (h[1] == 'E') SetIfAbsent(predicted, "E", "+");
                if (h[1] == 'e') SetIfAbsent(predicted, "e", "+");
            }

            if (rhdExpress.Count > 0)
                SetIfAbsent(predicted, "D", rhdExpress.Any(x => x) ? "+" : "-");

            ApplyPair(predicted, fy, "Fya", "Fyb", "a", "b");
            ApplyPair(predicted, jk, "Jka", "Jkb", "a", "b");

            if (kel.Count > 0)
            {
                SetIfAbsent(predicted, "K", kel.Contains("K") ? "+" : "-");
                SetIfAbsent(predicted, "k", kel.Contains("k") ? "+" : "-");
            }

            if (gypa.Count > 0)
            {
                SetIfAbsent(predicted, "M", gypa.Contains("M") ? "+" : "-");
                SetIfAbsent(predicted, "N", gypa.Contains("N") ? "+" : "-");
            }

            if (gypb.Count > 0)
            {
                SetIfAbsent(predicted, "S", gypb.Contains("S") ? "+" : "-");
                SetIfAbsent(predicted, "s", gypb.Contains("s") ? "+" : "-");
            }
        }

        private static void ApplyPair(Dictionary<string, string> predicted, List<string> alleles,
            string aName, string bName, string aCode, string bCode)
        {
            if (alleles.Count == 0) return;
            SetIfAbsent(predicted, aName, alleles.Contains(aCode) ? "+" : "-");
            SetIfAbsent(predicted, bName, alleles.Contains(bCode) ? "+" : "-");
        }

        private static string? NormalizeRhce(string allele)
        {
            var core = allele.Split('.')[0];
            if (core is "01" or "1") return "ce";
            if (core is "02" or "2") return "Ce";
            if (core is "03" or "3") return "cE";
            if (core is "04" or "4") return "CE";
            // C vs c is case-sensitive. IgnoreCase "ce" would turn CeRN and CE into ce.
            if (core.Length >= 2 &&
                (core[0] is 'C' or 'c') &&
                (core[1] is 'E' or 'e'))
            {
                return string.Concat(core[0] == 'C' ? 'C' : 'c', core[1] == 'E' ? 'E' : 'e');
            }
            return null;
        }

        private static string NormalizeKey(string allele) =>
            allele.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);

        private static string? NormalizeFyJk(string allele, string aLetter, string bLetter)
        {
            var upper = allele.ToUpperInvariant();
            var core = upper.Split('.')[0];
            var isNull = aLetter == "A"
                ? AlleleVariantCatalog.IsFyNull(allele)
                : AlleleVariantCatalog.IsJkNull(allele);
            if (core.StartsWith("01", StringComparison.Ordinal) || core == "1" || core == aLetter)
                return isNull ? "aN" : "a";
            if (core.StartsWith("02", StringComparison.Ordinal) || core == "2" || core == bLetter)
                return isNull ? "bN" : "b";
            return null;
        }

        private static string? NormalizeKel(string allele)
        {
            var core = allele.Split('.')[0];
            if (AlleleVariantCatalog.IsKelNull(allele))
            {
                var u = NormalizeKey(allele);
                return u.Contains("01N") || u.StartsWith("1N") ? "KN" : "kN";
            }
            if (core is "01" or "1" or "K") return "K";
            if (core is "02" or "2" or "k" or "KEL2") return "k";
            return null;
        }

        private static string? NormalizeGypa(string allele)
        {
            if (AlleleVariantCatalog.IsGypaNull(allele))
            {
                var u = allele.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
                if (u.Contains("01N")) return "M0";
                if (u.Contains("02N")) return "N0";
                return "GP0";
            }
            var core = allele.Split('.')[0];
            if (core.Equals("M", StringComparison.OrdinalIgnoreCase) || core is "01" or "1") return "M";
            if (core.Equals("N", StringComparison.OrdinalIgnoreCase) || core is "02" or "2") return "N";
            return null;
        }

        private static string? NormalizeGypb(string allele)
        {
            if (AlleleVariantCatalog.IsGypbNullOrUvar(allele))
            {
                var u = NormalizeKey(allele);
                if (u.Contains("03N")) return "SN";
                if (u.Contains("04N")) return "sN";
                return "UN";
            }
            var core = allele.Split('.')[0];
            if (core == "S" || core is "03" or "3") return "S";
            if (core == "s" || core is "04" or "4") return "s";
            return null;
        }

        private static void ApplyWeiner(string? phenotype, Dictionary<string, string> antigens)
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
                ApplyLooseAntigens(bestProfile, antigens);
        }

        private static bool ContainsWeiner(string compact, string code)
        {
            var idx = compact.IndexOf(code, StringComparison.OrdinalIgnoreCase);
            return idx >= 0;
        }

        private static void ApplySystemPairs(string? phenotype, Dictionary<string, string> antigens)
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
                SetIfKnown(antigens, prefix + "a", NormalizeSign(m.Groups[2].Value));
                if (m.Groups[3].Success)
                    SetIfKnown(antigens, prefix + "b", NormalizeSign(m.Groups[3].Value));
            }
        }

        private static void ApplyLooseAntigens(string? phenotype, Dictionary<string, string> antigens)
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
                        SetIfKnown(antigens, name, NormalizeSign(spaced.Groups[1].Value));
                        continue;
                    }
                }
                SetIfKnown(antigens, name, NormalizeSign(match.Groups[1].Value));
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

        private static void SetIfKnown(Dictionary<string, string> antigens, string antigen, string value)
        {
            var resolved = ResolveAntigen(antigen);
            if (resolved == null) return;
            antigens[resolved] = value;
        }

        private static void SetIfAbsent(Dictionary<string, string> antigens, string antigen, string value)
        {
            var resolved = ResolveAntigen(antigen);
            if (resolved == null || antigens.ContainsKey(resolved)) return;
            antigens[resolved] = value;
        }

        private static string? ResolveAntigen(string antigen)
        {
            var resolved = AntigenConstants.AllKnownAntigens
                .FirstOrDefault(a => string.Equals(a, antigen, StringComparison.Ordinal));
            return resolved ?? AntigenConstants.AllKnownAntigens
                .FirstOrDefault(a => string.Equals(a, antigen, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSign(string raw)
        {
            var c = raw.Trim();
            return c is "-" or "−" or "–" ? "-" : "+";
        }

        private static int KindOrder(PatientTypingKind kind) => kind switch
        {
            PatientTypingKind.Against => 0,
            PatientTypingKind.Predicted => 1,
            PatientTypingKind.Variant => 2,
            PatientTypingKind.Uninterpretable => 3,
            PatientTypingKind.Historical => 4,
            _ => 4
        };
    }
}
