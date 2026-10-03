using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public sealed class PanelCellTypingDiff
    {
        public string CellNumber { get; init; } = "";
        public string Antigen { get; init; } = "";
        public string Left { get; init; } = "NT";
        public string Right { get; init; } = "NT";
        public string Note { get; init; } = "";
    }

    public sealed class PanelCompareResult
    {
        public string LeftLabel { get; init; } = "";
        public string RightLabel { get; init; } = "";
        public SchemaDiff Schema { get; init; } = new();
        public List<string> OnlyLeftCells { get; } = new();
        public List<string> OnlyRightCells { get; } = new();
        public List<PanelCellTypingDiff> TypingDiffs { get; } = new();
        public string Explanation { get; set; } = "";
        public bool HasDifferences =>
            Schema.Changed || OnlyLeftCells.Count > 0 || OnlyRightCells.Count > 0 || TypingDiffs.Count > 0;
    }

    /// <summary>
    /// Compares two stored panel antigrams. Untyped antigens stay NT; they are
    /// never treated as negative.
    /// </summary>
    public static class PanelComparer
    {
        public static PanelCompareResult Compare(
            string leftLabel,
            IReadOnlyList<PanelCell> leftCells,
            IReadOnlyList<string> leftAntigens,
            string rightLabel,
            IReadOnlyList<PanelCell> rightCells,
            IReadOnlyList<string> rightAntigens)
        {
            var left = Index(leftCells);
            var right = Index(rightCells);
            var leftAgs = Normalize(leftAntigens, left.Values);
            var rightAgs = Normalize(rightAntigens, right.Values);
            var result = new PanelCompareResult
            {
                LeftLabel = leftLabel,
                RightLabel = rightLabel,
                Schema = PanelImportReviewer.CompareSchema(rightAgs, leftAgs)
            };

            foreach (var cell in left.Keys.Except(right.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(CellKey))
                result.OnlyLeftCells.Add(cell);
            foreach (var cell in right.Keys.Except(left.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(CellKey))
                result.OnlyRightCells.Add(cell);

            var antigens = leftAgs.Union(rightAgs, StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var cellNumber in left.Keys.Intersect(right.Keys, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(CellKey))
            {
                var leftCell = left[cellNumber];
                var rightCell = right[cellNumber];
                foreach (var ag in antigens)
                {
                    var lv = Mark(leftCell, ag);
                    var rv = Mark(rightCell, ag);
                    if (string.Equals(lv, rv, StringComparison.OrdinalIgnoreCase)) continue;
                    result.TypingDiffs.Add(new PanelCellTypingDiff
                    {
                        CellNumber = leftCell.CellNumber,
                        Antigen = ag,
                        Left = lv,
                        Right = rv,
                        Note = DescribeTyping(leftCell.CellNumber, ag, lv, rv)
                    });
                }
            }

            result.Explanation = Explain(result);
            return result;
        }

        public static string PanelLabel(Panel? panel)
        {
            if (panel == null) return "panel";
            var lot = string.IsNullOrWhiteSpace(panel.LotNumber) ? "" : " lot " + panel.LotNumber.Trim();
            return (panel.Name ?? "panel").Trim() + lot;
        }

        private static string Explain(PanelCompareResult result)
        {
            var sb = new StringBuilder();
            sb.Append(result.RightLabel).Append(" compared with ").Append(result.LeftLabel).Append('.');
            if (!result.HasDifferences)
            {
                sb.Append(" Antigen schema and typed cell values match. This is a review aid, not a diagnosis.");
                return sb.ToString();
            }
            if (result.Schema.Changed)
            {
                sb.Append(" Antigen schema differs.");
                if (result.Schema.Added.Count > 0)
                    sb.Append(" Added on the comparison panel: ").Append(string.Join(", ", result.Schema.Added)).Append('.');
                if (result.Schema.Removed.Count > 0)
                    sb.Append(" Missing from the comparison panel: ").Append(string.Join(", ", result.Schema.Removed)).Append('.');
            }
            else
            {
                sb.Append(" Antigen schema matches.");
            }
            if (result.OnlyRightCells.Count > 0)
                sb.Append(" Cells only on the comparison panel: ").Append(string.Join(", ", result.OnlyRightCells)).Append('.');
            if (result.OnlyLeftCells.Count > 0)
                sb.Append(" Cells only on the selected panel: ").Append(string.Join(", ", result.OnlyLeftCells)).Append('.');
            if (result.TypingDiffs.Count > 0)
            {
                sb.Append(' ').Append(result.TypingDiffs.Count).Append(" typing difference")
                    .Append(result.TypingDiffs.Count == 1 ? "" : "s").Append(" on shared cells.");
                foreach (var diff in result.TypingDiffs.Take(6))
                    sb.Append(' ').Append(diff.Note);
                if (result.TypingDiffs.Count > 6)
                    sb.Append(" …");
            }
            sb.Append(" Review both antigrams before activating a lot. This is not a diagnosis.");
            return sb.ToString();
        }

        private static string DescribeTyping(string cell, string antigen, string left, string right) =>
            $"Cell {cell} {antigen} is {left} on the selected panel and {right} on the comparison panel.";

        private static string Mark(PanelCell cell, string antigen) =>
            cell.HasTypedAntigen(antigen) ? cell.GetAntigen(antigen) : "NT";

        private static Dictionary<string, PanelCell> Index(IEnumerable<PanelCell> cells)
        {
            var map = new Dictionary<string, PanelCell>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in cells)
            {
                if (string.IsNullOrWhiteSpace(cell.CellNumber)) continue;
                map[cell.CellNumber.Trim()] = cell;
            }
            return map;
        }

        private static List<string> Normalize(IReadOnlyList<string> listed, IEnumerable<PanelCell> cells)
        {
            var fromList = listed
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (fromList.Count > 0) return fromList;
            return cells
                .SelectMany(c => c.Antigens.Where(kv => AntigenConstants.IsTypedAntigenValue(kv.Value))
                    .Select(kv => kv.Key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int CellKey(string cellNumber) =>
            string.Equals(cellNumber, "AC", StringComparison.OrdinalIgnoreCase) ? int.MaxValue
            : int.TryParse(cellNumber, out var n) ? n : int.MaxValue - 1;
    }
}
