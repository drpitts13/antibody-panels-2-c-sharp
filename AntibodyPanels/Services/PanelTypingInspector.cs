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
    }
}
