using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AntibodyPanels.Services
{
    public sealed class AlleleVariantNote
    {
        public string Antibody { get; init; } = string.Empty;
        public string Antigen { get; init; } = string.Empty;
        public string Allele { get; init; } = string.Empty;
        public string Explanation { get; init; } = string.Empty;
        public bool SuppressPredictedSupport { get; init; }
    }

    /// <summary>
    /// Reviewable transfusion-risk notes for a small set of well-known alleles.
    /// This is not a diagnosis and does not change suspected or ruled-out lists.
    /// </summary>
    public static class AlleleVariantCatalog
    {
        private static readonly Regex Token = new(
            @"\b(RHCE|RHD|FY|JK|KEL|GYPA|GYPB)\s*\*\s*([A-Za-z0-9.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex JkPair = new(
            @"\bJK\s*\*\s*([A-Za-z0-9.]+)\s*/\s*(?:JK\s*\*\s*)?([A-Za-z0-9.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<AlleleVariantNote> Match(string? genotype)
        {
            var notes = new List<AlleleVariantNote>();
            if (string.IsNullOrWhiteSpace(genotype)) return notes;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in Token.Matches(genotype))
            {
                var system = m.Groups[1].Value.ToUpperInvariant();
                var allele = m.Groups[2].Value.Trim();
                var key = system + "*" + allele;
                if (!seen.Add(system + "*" + NormalizeKey(allele))) continue;
                var note = Describe(system, allele);
                if (note != null) notes.Add(note);
            }

            if (Regex.IsMatch(genotype, @"\bGATA\b", RegexOptions.IgnoreCase) &&
                notes.TrueForAll(n => n.Antigen != "Fya"))
            {
                notes.Add(GataNote("GATA"));
            }

            if (JkNullAlleleCount(genotype) >= 2 &&
                notes.TrueForAll(n => n.Antibody != "anti-Jk3"))
            {
                notes.Add(Jk3Note());
            }

            if (IsGypaEnaRisk(genotype) &&
                notes.TrueForAll(n => n.Antibody != "anti-Ena"))
            {
                notes.Add(EnaNote());
            }

            return notes;
        }

        public static bool IsRhdNull(string allele)
        {
            var u = NormalizeKey(allele);
            if (IsRhdPartialOrWeak(u)) return false;
            return u.Contains("01N") || u.StartsWith("N") ||
                   u.Contains("DEL") || u.Contains("NULL") ||
                   u is "NEGATIVE" or "NEG";
        }

        /// <summary>
        /// Partial D (DAR, DVI, DNB, …). May type D+ and still make alloanti-D.
        /// </summary>
        public static bool IsRhdPartial(string allele)
        {
            var u = NormalizeKey(allele);
            if (IsRhdWeak(u) || u.Contains("EL") || u.Contains("DEL")) return false;
            return u.Contains("DAR") || u.Contains("DVI") || u.Contains("DVII") ||
                   u.Contains("DIII") || u.Contains("DIV") ||
                   u == "DV" || u.StartsWith("DV.") ||
                   u.Contains("DFR") || u.Contains("DAU") ||
                   u.Contains("DNB") || u.Contains("DOL") ||
                   u.Contains("DBT") || u.Contains("DHAR") ||
                   u.Contains("DCS") || u.Contains("DTO");
        }

        public static bool IsRhdWeak(string allele)
        {
            var u = NormalizeKey(allele);
            return u.Contains("01W") || u.Contains("WEAK");
        }

        public static bool IsRhdPartialOrWeak(string allele)
        {
            var u = NormalizeKey(allele);
            return IsRhdPartial(u) || IsRhdWeak(u) ||
                   u.Contains("EL") || u.Contains("DEL") ||
                   u.Contains("DVI") || u.Contains("DVII") ||
                   u.Contains("DFR") || u.Contains("DAU") ||
                   u.Contains("DNB") || u.Contains("DOL");
        }

        public static bool IsRhceVariantE(string allele)
        {
            var u = NormalizeKey(allele);
            return u.Contains("AR") || u.Contains("EK") || u.Contains("MO") ||
                   u.Contains("VS") || u.Contains("CF") || u.Contains("AG");
        }

        /// <summary>
        /// Partial / variant C (CeRN, HAR, C^w). Predicted C+ does not rule out alloanti-C.
        /// </summary>
        public static bool IsRhceVariantC(string allele)
        {
            var u = NormalizeKey(allele);
            if (IsRhceVariantE(u)) return false;
            return u.Contains("CERN") || u.Contains("HAR") || u.Contains("CW");
        }

        public static bool IsGataSilencing(string allele)
        {
            var u = NormalizeKey(allele);
            return u.StartsWith("01N") || u.Contains("GATA");
        }

        public static bool IsFyNull(string allele) =>
            IsGataSilencing(allele) || NormalizeKey(allele).Contains("02N");

        public static bool IsJkWeak(string allele)
        {
            var u = NormalizeKey(allele);
            return u.Contains("01W") || u.Contains("02W") || u.Contains("WEAK");
        }

        public static bool IsJkNull(string allele)
        {
            var u = NormalizeKey(allele);
            if (IsJkWeak(u)) return false;
            return u.Contains("01N") || u.Contains("02N") ||
                   u.Contains("NULL") || u is "NEGATIVE" or "NEG";
        }

        public static bool IsKelNull(string allele)
        {
            var u = NormalizeKey(allele);
            if (u.Contains("02M") || u.Contains("KMOD")) return false;
            return u.Contains("01N") || u.Contains("02N") ||
                   u.Contains("K0") || u.Contains("KNULL") || u.Contains("NULL");
        }

        public static bool IsGypaNull(string allele)
        {
            var u = NormalizeKey(allele);
            if (u.Contains("01W") || u.Contains("02W") || u.Contains("WEAK")) return false;
            return u.Contains("01N") || u.Contains("02N") ||
                   u.Contains("NULL") || u.Contains("ENA") ||
                   u is "MK" or "MKMK" or "NEGATIVE" or "NEG";
        }

        public static bool IsGypbNullOrUvar(string allele)
        {
            var u = NormalizeKey(allele);
            return u.Contains("03N") || u.Contains("04N") ||
                   u.Contains("UNULL") || u.Contains("UVAR") ||
                   u.Contains("DELETION") || u is "U-" or "UNEG";
        }

        private static AlleleVariantNote? Describe(string system, string allele)
        {
            var u = NormalizeKey(allele);
            return system switch
            {
                "FY" when IsGataSilencing(allele) => GataNote("FY*" + allele),
                "FY" when u.StartsWith("02M") || u is "X" or "02X" => new AlleleVariantNote
                {
                    Antibody = "anti-Fyb",
                    Antigen = "Fyb",
                    Allele = "FY*" + allele,
                    Explanation =
                        $"FY*{allele} (FyX / FY*02M) often types Fy(b+^w). A weak Fyb type is not the same as Fy(b−) and does not identify anti-Fyb. Not a diagnosis."
                },
                "RHD" when u.Contains("EL") || u.Contains("DEL") => new AlleleVariantNote
                {
                    Antibody = "anti-D",
                    Antigen = "D",
                    Allele = "RHD*" + allele,
                    Explanation =
                        $"RHD*{allele} is a DEL / very weak D variant. Routine serology may type D−. Review anti-D risk and do not treat a D− tube type as a complete genotype. Not a diagnosis."
                },
                "RHD" when IsRhdPartial(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-D",
                    Antigen = "D",
                    Allele = "RHD*" + allele,
                    Explanation =
                        $"RHD*{allele} is a partial D variant (DAR / DVI / DNB-like). The patient may type D+ with some reagents and still make alloanti-D. Serologic or predicted D+ does not rule out anti-D. Not a diagnosis."
                },
                "RHD" when IsRhdWeak(allele) || IsRhdPartialOrWeak(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-D",
                    Antigen = "D",
                    Allele = "RHD*" + allele,
                    Explanation =
                        $"RHD*{allele} is a weak D or partial D variant. Serologic D type and anti-D risk need review; reagent-dependent D typing is common. Not a diagnosis."
                },
                "RHCE" when IsRhceVariantC(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-C",
                    Antigen = "C",
                    Allele = "RHCE*" + allele,
                    Explanation =
                        $"RHCE*{allele} is a variant C haplotype (CeRN / HAR / C^w-like). The patient may type C+ and still make a C-like alloantibody. Predicted C+ does not rule out anti-C. Not a diagnosis."
                },
                "RHCE" when IsRhceVariantE(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-e",
                    Antigen = "e",
                    Allele = "RHCE*" + allele,
                    Explanation =
                        $"RHCE*{allele} is a variant e haplotype. The patient may type e+ and still make an e-like alloantibody. Predicted e+ does not rule out anti-e. Not a diagnosis."
                },
                "JK" when IsJkNull(allele) => JkNullNote(allele),
                "JK" when IsJkWeak(allele) => JkWeakNote(allele),
                "KEL" when IsKelNull(allele) || u.Contains("02M") || u.Contains("KMOD") => new AlleleVariantNote
                {
                    Antibody = u.Contains("01N") ? "anti-K" : "anti-k",
                    Antigen = u.Contains("01N") ? "K" : "k",
                    Allele = "KEL*" + allele,
                    Explanation =
                        $"KEL*{allele} is a K0 / Kell-null or Kmod allele. Serologic K/k typing may be weak or absent; a K0 phenotype can make anti-Ku. Not a diagnosis."
                },
                "GYPB" when IsGypbNullOrUvar(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-U",
                    Antigen = "U",
                    Allele = "GYPB*" + allele,
                    Explanation =
                        $"GYPB*{allele} is a GYPB null / U variant. S/s typing may be absent and anti-U risk needs review. Predicted S− or s− is not an identification. Not a diagnosis."
                },
                "GYPA" when IsGypaNull(allele) => GypaNullNote(allele),
                _ => null
            };
        }

        private static AlleleVariantNote GataNote(string allele) => new()
        {
            Antibody = "anti-Fya",
            Antigen = "Fya",
            Allele = allele,
            SuppressPredictedSupport = true,
            Explanation =
                "FY*01N (GATA) silences Fya on red cells only; tissue Fya is usually present. " +
                "Predicted RBC Fy(a−) does not support alloanti-Fya the way a true Fy(a−) type would. Not a diagnosis."
        };

        private static AlleleVariantNote JkNullNote(string allele)
        {
            var u = NormalizeKey(allele);
            var isB = u.Contains("02N") || u.StartsWith("B");
            return new AlleleVariantNote
            {
                Antibody = isB ? "anti-Jkb" : "anti-Jka",
                Antigen = isB ? "Jkb" : "Jka",
                Allele = "JK*" + allele,
                Explanation =
                    $"JK*{allele} is a true Jk null (RBC and tissue). Unlike GATA-FY, predicted Jk− can support an alloantibody to that antigen. Not a diagnosis."
            };
        }

        private static AlleleVariantNote JkWeakNote(string allele)
        {
            var u = NormalizeKey(allele);
            var isB = u.Contains("02W") || u.StartsWith("B");
            var ag = isB ? "Jkb" : "Jka";
            return new AlleleVariantNote
            {
                Antibody = "anti-" + ag,
                Antigen = ag,
                Allele = "JK*" + allele,
                Explanation =
                    $"JK*{allele} is a weak Jk allele. The patient may type {ag}+^w or {ag}− with some reagents and still make an alloanti-{ag}. Predicted {ag}+ does not rule out anti-{ag}. Not a diagnosis."
            };
        }

        private static AlleleVariantNote GypaNullNote(string allele)
        {
            var u = NormalizeKey(allele);
            var isN = u.Contains("02N") || u.StartsWith("N");
            var ag = isN ? "N" : "M";
            return new AlleleVariantNote
            {
                Antibody = "anti-" + ag,
                Antigen = ag,
                Allele = "GYPA*" + allele,
                Explanation =
                    $"GYPA*{allele} is a GYPA null / Mk-related allele. Predicted {ag}− can support alloanti-{ag}. " +
                    "It is not the same as an untyped MNS column. Not a diagnosis."
            };
        }

        private static AlleleVariantNote EnaNote() => new()
        {
            Antibody = "anti-Ena",
            Antigen = "Ena",
            Allele = "GYPA*null",
            Explanation =
                "GYPA null / Mk alleles predict M−N−. Review for anti-En(a) (high-prevalence on GYPA) in addition to anti-M or anti-N. This is not an identification."
        };

        private static bool IsGypaEnaRisk(string genotype)
        {
            var alleles = new List<string>();
            foreach (Match m in Token.Matches(genotype))
            {
                if (m.Groups[1].Value.Equals("GYPA", StringComparison.OrdinalIgnoreCase))
                    alleles.Add(m.Groups[2].Value);
            }
            if (alleles.Count == 0) return false;
            if (!alleles.TrueForAll(IsGypaNull)) return false;
            return alleles.Count >= 2 || alleles.Exists(a =>
            {
                var u = NormalizeKey(a);
                return u is "MK" or "MKMK" || u.Contains("ENA");
            });
        }

        private static AlleleVariantNote Jk3Note() => new()
        {
            Antibody = "anti-Jk3",
            Antigen = "Jk3",
            Allele = "JK*null/null",
            Explanation =
                "Two JK null alleles predict Jk(a−b−). Review for anti-Jk3 (high-prevalence) in addition to anti-Jka or anti-Jkb. This is not an identification."
        };

        private static int JkNullAlleleCount(string genotype)
        {
            var alleles = new List<string>();
            foreach (Match m in JkPair.Matches(genotype))
            {
                alleles.Add(m.Groups[1].Value);
                alleles.Add(m.Groups[2].Value);
            }
            if (alleles.Count == 0)
            {
                foreach (Match m in Token.Matches(genotype))
                {
                    if (m.Groups[1].Value.Equals("JK", StringComparison.OrdinalIgnoreCase))
                        alleles.Add(m.Groups[2].Value);
                }
            }
            return alleles.Count(IsJkNull);
        }

        private static string NormalizeKey(string allele) =>
            allele.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
    }
}
