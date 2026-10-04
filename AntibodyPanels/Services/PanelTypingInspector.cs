using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;
using AntibodyPanels.Services.Vendors;

namespace AntibodyPanels.Services
{
    public sealed class PanelTypingIssue
    {
        public string Kind { get; init; } = string.Empty;
        public string? CellNumber { get; init; }
        public string Explanation { get; init; } = string.Empty;
    }

    /// <summary>
    /// Reviewable typing problems on an imported antigram. Does not change
    /// stored types or identify an antibody.
    /// </summary>
    public static class PanelTypingInspector
    {
        public const string ImpossibleValue = "ImpossibleValue";
        public const string UnknownAntigen = "UnknownAntigen";
        public const string InvalidValue = "InvalidValue";
        public const string NullPhenotype = "NullPhenotype";

        private static readonly HashSet<string> MetadataKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "CELL", "CELLNUMBER", "CELLNO", "NO", "NUMBER", "VIAL",
            "DONOR", "DONORID", "DONORNO",
            "RH", "RHHR", "PHENOTYPE", "RHPHENOTYPE",
            "SPECIALTYPES", "NOTES", "SPECIAL",
            "ABO", "GROUP", "BLOODGROUP",
            "LOT", "LOTNUMBER", "EXP", "EXPIRATION", "EXPIRATIONDATE",
            "NAME", "CATALOG", "CATALOGNUMBER", "PRODUCT", "PRODUCTLINE",
            "VENDOR", "MANUFACTURER"
        };

        public static List<PanelTypingIssue> Inspect(
            IEnumerable<PanelCell>? cells, IEnumerable<string>? extraHeaders = null)
        {
            var issues = new List<PanelTypingIssue>();
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in extraHeaders ?? Array.Empty<string>())
            {
                if (TryUnknownHeader(header, out var label))
                    unknown.Add(label);
            }

            foreach (var cell in cells ?? Array.Empty<PanelCell>())
            {
                foreach (var (ag, raw) in cell.Antigens)
                {
                    if (TryUnknownHeader(ag, out var label))
                        unknown.Add(label);
                    if (IsInvalidAntigenValue(raw))
                    {
                        issues.Add(new PanelTypingIssue
                        {
                            Kind = InvalidValue,
                            CellNumber = cell.CellNumber,
                            Explanation =
                                $"Cell {cell.CellNumber} has unrecognized type '{raw}' for {ag}."
                        });
                    }
                }
                AddHomozygousConflicts(cell, issues);
                AddNullPhenotypes(cell, issues);
            }

            if (unknown.Count > 0)
            {
                issues.Insert(0, new PanelTypingIssue
                {
                    Kind = UnknownAntigen,
                    Explanation =
                        "Unknown antigen columns were not imported: " +
                        string.Join(", ", unknown.OrderBy(a => a, StringComparer.OrdinalIgnoreCase)) +
                        "."
                });
            }
            return issues;
        }

        public static bool TryRecordUnknownHeader(string? header, ICollection<string> into)
        {
            if (!TryUnknownHeader(header, out var label)) return false;
            if (into.Any(x => string.Equals(x, label, StringComparison.OrdinalIgnoreCase)))
                return false;
            into.Add(label);
            return true;
        }

        public static bool IsMetadataColumn(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return true;
            return MetadataKeys.Contains(VendorAntigenAliases.NormalizeKey(header));
        }

        private static bool TryUnknownHeader(string? header, out string label)
        {
            label = "";
            if (string.IsNullOrWhiteSpace(header) || IsMetadataColumn(header))
                return false;
            if (VendorAntigenAliases.Resolve(header) != null)
                return false;
            if (AntigenConstants.IsKnown(header.Trim()))
                return false;
            label = header.Trim();
            return true;
        }

        private static bool IsInvalidAntigenValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var v = raw.Trim();
            if (v == AntigenConstants.AntigenNotTested || v == "?") return false;
            return !AntigenConstants.IsTypedAntigenValue(v);
        }

        private static void AddHomozygousConflicts(PanelCell cell, List<PanelTypingIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (ag, partner) in AntigenConstants.AntitheticalPairs)
            {
                if (string.CompareOrdinal(ag, partner) > 0) continue;
                var key = ag + "|" + partner;
                if (!seen.Add(key)) continue;
                var aHomo = cell.IsExplicitlyHomozygous(ag);
                var bHomo = cell.IsExplicitlyHomozygous(partner);
                var aPos = cell.IsAntigenPositive(ag);
                var bPos = cell.IsAntigenPositive(partner);
                if (aHomo && bPos)
                {
                    issues.Add(Conflict(cell.CellNumber, ag, partner));
                }
                else if (bHomo && aPos)
                {
                    issues.Add(Conflict(cell.CellNumber, partner, ag));
                }
            }
        }

        private static PanelTypingIssue Conflict(string cell, string homozygous, string partner) =>
            new()
            {
                Kind = ImpossibleValue,
                CellNumber = cell,
                Explanation =
                    $"Cell {cell} marks {homozygous} homozygous (++) while {partner} is also positive — that combination is not possible."
            };

        /// <summary>
        /// Both antitheticals typed negative can be a true null reagent cell
        /// or a mistype. Lewis Lea−Leb− is common and is not flagged.
        /// Untyped partners are not treated as negative.
        /// </summary>
        private static void AddNullPhenotypes(PanelCell cell, List<PanelTypingIssue> issues)
        {
            var cc = BothTypedNegative(cell, "C", "c");
            var ee = BothTypedNegative(cell, "E", "e");
            if (cc && ee)
            {
                issues.Add(NullNote(cell.CellNumber,
                    "types C−c− and E−e− (Rhnull / D−−-like). Confirm the antigram before using this cell for rule-out."));
            }
            else if (cc)
            {
                issues.Add(NullNote(cell.CellNumber,
                    "types C−c−. That pattern can be D−−, Rhnull, or a mistype. Confirm before using this cell."));
            }
            else if (ee)
            {
                issues.Add(NullNote(cell.CellNumber,
                    "types E−e−. That pattern can be a deleted RHCE haplotype or a mistype. Confirm before using this cell."));
            }

            foreach (var (a, b, clause) in NullPairs)
            {
                if (BothTypedNegative(cell, a, b))
                    issues.Add(NullNote(cell.CellNumber, clause));
            }
        }

        private static readonly (string A, string B, string Clause)[] NullPairs =
        {
            ("K", "k", "types K−k− (K0-like). Confirm Kell-null typing; this is not an identification."),
            ("Fya", "Fyb", "types Fy(a−b−). This may be GATA silencing or a true Fy3-null cell. Confirm before use."),
            ("Jka", "Jkb", "types Jk(a−b−). Rare Jk3-negative panel cells are useful for high-prevalence workups; confirm the typing."),
            ("S", "s", "types S−s− (U-var / GYPB-null-like). Confirm before using this cell."),
            ("M", "N", "types M−N− (Mk-like). Confirm before using this cell."),
            ("Lua", "Lub", "types Lu(a−b−) (In(Lu) / Lunull-like). Confirm before using this cell."),
            ("Kpa", "Kpb", "types Kp(a−b−). Confirm Kell-system null typing."),
            ("Jsa", "Jsb", "types Js(a−b−). Confirm Kell-system null typing.")
        };

        private static bool BothTypedNegative(PanelCell cell, string a, string b) =>
            cell.HasTypedAntigen(a) && cell.GetTypedValue(a) == "-" &&
            cell.HasTypedAntigen(b) && cell.GetTypedValue(b) == "-";

        private static PanelTypingIssue NullNote(string cell, string clause) =>
            new()
            {
                Kind = NullPhenotype,
                CellNumber = cell,
                Explanation = $"Cell {cell} {clause} Not a diagnosis."
            };
    }
}
