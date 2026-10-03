using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public readonly record struct AntigramFilterRow(
        string CellNumber,
        IReadOnlyDictionary<string, string> Antigens,
        bool IsReactive);

    /// <summary>
    /// Filters and sorts antigram rows for display only. Hidden cells stay
    /// in the working set for save and analysis.
    /// </summary>
    public static class AntigramRowFilter
    {
        public const string AllCells = "All cells";
        public const string SortCellNumber = "Cell number";
        public const string SortReactiveFirst = "Reactive first";
        public const string SortAntigenFirst = "Selected antigen first";

        public static readonly IReadOnlyList<string> SortOptions = new[]
        {
            SortCellNumber, SortReactiveFirst, SortAntigenFirst
        };

        public static bool IsSpecificAntigen(string? antigen) =>
            !string.IsNullOrWhiteSpace(antigen)
            && !antigen.Equals(AllCells, StringComparison.OrdinalIgnoreCase);

        public static bool Matches(AntigramFilterRow row, string? antigen, string? zygosity)
        {
            if (IsAutocontrol(row.CellNumber)) return true;
            if (!IsSpecificAntigen(antigen)) return true;

            var classified = AntigramDisplay.Classify(row.Antigens, antigen!);
            if (classified is AntigenZygosity.NotTested or AntigenZygosity.Negative)
                return false;
            if (string.Equals(zygosity, AntigenConstants.ZygosityHomozygous, StringComparison.OrdinalIgnoreCase))
                return classified == AntigenZygosity.Homozygous;
            if (string.Equals(zygosity, AntigenConstants.ZygosityHeterozygous, StringComparison.OrdinalIgnoreCase))
                return classified == AntigenZygosity.Heterozygous;
            return true;
        }

        public static IReadOnlyList<AntigramFilterRow> Apply(
            IEnumerable<AntigramFilterRow> rows,
            string? antigen,
            string? zygosity,
            string? sort)
        {
            var matched = rows.Where(r => Matches(r, antigen, zygosity)).ToList();
            return matched
                .OrderBy(r => IsAutocontrol(r.CellNumber) ? 1 : 0)
                .ThenBy(r => SortRank(r, antigen, sort))
                .ThenBy(r => CellSortKey(r.CellNumber))
                .ToList();
        }

        public static string Explain(
            IReadOnlyList<AntigramFilterRow> visible,
            int total,
            string? antigen,
            string? zygosity,
            string? sort)
        {
            var ac = visible.Any(r => IsAutocontrol(r.CellNumber));
            var shown = visible.Count;
            var body = !IsSpecificAntigen(antigen)
                ? $"Showing all {total} cells"
                : zygosity switch
                {
                    AntigenConstants.ZygosityHomozygous =>
                        $"Showing {shown} of {total} cells that are homozygous {antigen}+",
                    AntigenConstants.ZygosityHeterozygous =>
                        $"Showing {shown} of {total} cells that are heterozygous {antigen}+",
                    _ =>
                        $"Showing {shown} of {total} cells that are {antigen}+"
                };
            if (IsSpecificAntigen(antigen) && ac)
                body += " (autocontrol always shown)";
            var sortLabel = sort switch
            {
                SortReactiveFirst => "reactive first",
                SortAntigenFirst when IsSpecificAntigen(antigen) => $"{antigen}+ first",
                _ => "cell number"
            };
            return $"{body}, sorted by {sortLabel}.";
        }

        private static int SortRank(AntigramFilterRow row, string? antigen, string? sort)
        {
            if (IsAutocontrol(row.CellNumber)) return 0;
            if (string.Equals(sort, SortReactiveFirst, StringComparison.OrdinalIgnoreCase))
                return row.IsReactive ? 0 : 1;
            if (string.Equals(sort, SortAntigenFirst, StringComparison.OrdinalIgnoreCase)
                && IsSpecificAntigen(antigen))
            {
                var z = AntigramDisplay.Classify(row.Antigens, antigen!);
                return z switch
                {
                    AntigenZygosity.Homozygous => 0,
                    AntigenZygosity.Heterozygous => 1,
                    AntigenZygosity.PositiveUnknown => 2,
                    _ => 3
                };
            }
            return 0;
        }

        private static int CellSortKey(string cellNumber)
        {
            if (IsAutocontrol(cellNumber)) return int.MaxValue;
            return int.TryParse(cellNumber, out var n) ? n : int.MaxValue - 1;
        }

        private static bool IsAutocontrol(string? cellNumber) =>
            string.Equals(cellNumber, "AC", StringComparison.OrdinalIgnoreCase);
    }
}
