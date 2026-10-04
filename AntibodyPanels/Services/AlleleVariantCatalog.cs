using System;
using System.Collections.Generic;
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
            @"\b(RHCE|RHD|FY|JK|KEL)\s*\*\s*([A-Za-z0-9.]+)",
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

        public static bool IsRhdPartialOrWeak(string allele)
        {
            var u = NormalizeKey(allele);
            return u.Contains("01W") || u.Contains("WEAK") ||
                   u.Contains("DVI") || u.Contains("DVII") ||
                   u.Contains("DFR") || u.Contains("DAU") ||
                   u.Contains("DNB") || u.Contains("DOL") ||
                   u.Contains("EL");
        }

        public static bool IsGataSilencing(string allele)
        {
            var u = NormalizeKey(allele);
            return u.StartsWith("01N") || u.Contains("GATA");
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
                "RHD" when IsRhdPartialOrWeak(allele) => new AlleleVariantNote
                {
                    Antibody = "anti-D",
                    Antigen = "D",
                    Allele = "RHD*" + allele,
                    Explanation =
                        $"RHD*{allele} is a weak D or partial D variant. Serologic D type and anti-D risk need review; reagent-dependent D typing is common. Not a diagnosis."
                },
                "RHCE" when u.Contains("AR") || u.Contains("EK") || u.Contains("MO") ||
                            u.Contains("VS") || u.Contains("CF") || u.Contains("AG") => new AlleleVariantNote
                {
                    Antibody = "anti-e",
                    Antigen = "e",
                    Allele = "RHCE*" + allele,
                    Explanation =
                        $"RHCE*{allele} is a variant e haplotype. The patient may type e+ and still make an e-like alloantibody. Predicted e+ does not rule out anti-e. Not a diagnosis."
                },
                "KEL" when u.Contains("02M") || u.Contains("KMOD") || u.Contains("K0") => new AlleleVariantNote
                {
                    Antibody = "anti-k",
                    Antigen = "k",
                    Allele = "KEL*" + allele,
                    Explanation =
                        $"KEL*{allele} is a Kmod / K0-like allele. Serologic k typing may be weak or absent. Not a diagnosis."
                },
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

        private static string NormalizeKey(string allele) =>
            allele.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
    }
}
