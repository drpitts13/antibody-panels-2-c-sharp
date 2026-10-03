using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public enum AntigenZygosity
    {
        NotTested,
        Negative,
        Homozygous,
        Heterozygous,
        PositiveUnknown
    }

    /// <summary>
    /// Reviewable antigram presentation: blood-group column order and
    /// homozygous / heterozygous marks. Does not change rule-out.
    /// </summary>
    public static class AntigramDisplay
    {
        private static readonly string[] SystemOrder =
        {
            "Rh", "Kell", "Duffy", "Kidd", "MNS", "Lewis", "Lutheran", "P", "Xg"
        };

        private static readonly Dictionary<string, string> Systems =
            BuildSystems();

        public static string SystemOf(string antigen)
        {
            if (string.IsNullOrWhiteSpace(antigen)) return "Other";
            if (Systems.TryGetValue(antigen, out var system)) return system;
            var warehouse = AntigenConstants.WarehouseCatalog
                .FirstOrDefault(d => d.Name == antigen);
            return string.IsNullOrWhiteSpace(warehouse?.System) ? "Other" : warehouse!.System;
        }

        public static IReadOnlyList<string> GroupBySystem(IEnumerable<string> antigens)
        {
            var listed = antigens?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList()
                ?? new List<string>();
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < SystemOrder.Length; i++)
                rank[SystemOrder[i]] = i;
            return listed
                .Select((ag, idx) => new { ag, idx, system = SystemOf(ag) })
                .OrderBy(x => rank.TryGetValue(x.system, out var r) ? r : 100 + x.idx)
                .ThenBy(x => x.idx)
                .Select(x => x.ag)
                .ToList();
        }

        public static AntigenZygosity Classify(IReadOnlyDictionary<string, string> antigens, string antigen)
        {
            if (!Typed(antigens, antigen, out var value))
                return AntigenZygosity.NotTested;
            if (value == "-")
                return AntigenZygosity.Negative;
            if (!AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var partner))
                return AntigenZygosity.PositiveUnknown;
            if (!Typed(antigens, partner, out var partnerValue))
                return AntigenZygosity.PositiveUnknown;
            return partnerValue == "-" ? AntigenZygosity.Homozygous : AntigenZygosity.Heterozygous;
        }

        public static AntigenZygosity Classify(PanelCell cell, string antigen) =>
            Classify(cell.Antigens, antigen);

        public static string Format(IReadOnlyDictionary<string, string> antigens, string antigen, bool showDosage)
        {
            var zygosity = Classify(antigens, antigen);
            if (!showDosage)
            {
                return zygosity switch
                {
                    AntigenZygosity.NotTested => "",
                    AntigenZygosity.Negative => "-",
                    _ => "+"
                };
            }

            return zygosity switch
            {
                AntigenZygosity.NotTested => "",
                AntigenZygosity.Negative => "-",
                AntigenZygosity.Homozygous => "++",
                AntigenZygosity.Heterozygous => "+",
                AntigenZygosity.PositiveUnknown =>
                    AntigenConstants.AntitheticalPairs.ContainsKey(antigen) ? "+/?" : "+",
                _ => ""
            };
        }

        public static string Explain(string cellNumber, IReadOnlyDictionary<string, string> antigens, string antigen)
        {
            var cell = string.IsNullOrWhiteSpace(cellNumber) ? "Cell" : "Cell " + cellNumber.Trim();
            var system = SystemOf(antigen);
            return Classify(antigens, antigen) switch
            {
                AntigenZygosity.NotTested =>
                    $"{cell} was not typed for {antigen} ({system}).",
                AntigenZygosity.Negative =>
                    $"{cell} is {antigen}− ({system}).",
                AntigenZygosity.Homozygous =>
                    $"{cell} is homozygous for {antigen} ({antigen}+ {Partner(antigen)}−; {system}).",
                AntigenZygosity.Heterozygous =>
                    $"{cell} is heterozygous for {antigen} ({antigen}+ {Partner(antigen)}+; {system}).",
                AntigenZygosity.PositiveUnknown when AntigenConstants.AntitheticalPairs.ContainsKey(antigen) =>
                    $"{cell} is {antigen}+ but {Partner(antigen)} was not typed, so zygosity is unknown ({system}).",
                _ =>
                    $"{cell} is {antigen}+ ({system})."
            };
        }

        private static string Partner(string antigen) =>
            AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var partner) ? partner : "?";

        private static bool Typed(IReadOnlyDictionary<string, string> antigens, string antigen, out string value)
        {
            value = "";
            if (!antigens.TryGetValue(antigen, out var raw) || raw == null)
                return false;
            value = raw;
            return AntigenConstants.IsTypedAntigenValue(value);
        }

        private static Dictionary<string, string> BuildSystems()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["D"] = "Rh", ["C"] = "Rh", ["c"] = "Rh", ["E"] = "Rh", ["e"] = "Rh",
                ["f"] = "Rh", ["Cw"] = "Rh", ["V"] = "Rh",
                ["K"] = "Kell", ["k"] = "Kell", ["Kpa"] = "Kell", ["Kpb"] = "Kell",
                ["Jsa"] = "Kell", ["Jsb"] = "Kell",
                ["Fya"] = "Duffy", ["Fyb"] = "Duffy",
                ["Jka"] = "Kidd", ["Jkb"] = "Kidd",
                ["M"] = "MNS", ["N"] = "MNS", ["S"] = "MNS", ["s"] = "MNS",
                ["Lea"] = "Lewis", ["Leb"] = "Lewis",
                ["Lua"] = "Lutheran", ["Lub"] = "Lutheran",
                ["P1"] = "P",
                ["Xga"] = "Xg"
            };
            foreach (var def in AntigenConstants.WarehouseCatalog)
            {
                if (!map.ContainsKey(def.Name) && !string.IsNullOrWhiteSpace(def.System))
                    map[def.Name] = def.System;
            }
            return map;
        }
    }
}
