using System.Collections.Generic;
using System.Linq;

namespace AntibodyPanels.Models
{
    public sealed class WarehouseAntigenDefinition
    {
        public string Name { get; init; } = string.Empty;
        public string System { get; init; } = string.Empty;
        public string? Antithetical { get; init; }
        public string TreatmentNote { get; init; } = string.Empty;

        public string DisplayLabel => $"{Name}  ({System})  —  {TreatmentNote}";
    }

    public static class AntigenConstants
    {
        public static readonly IReadOnlyList<string> Antigens = new[]
        {
            "D", "C", "c", "E", "e", "f", "Cw", "V",
            "K", "k", "Kpa", "Kpb", "Jsa", "Jsb",
            "Jka", "Jkb", "Fya", "Fyb",
            "Lea", "Leb", "M", "N", "S", "s",
            "Lua", "Lub", "Xga", "P1"
        };

        /// <summary>
        /// Antigens that must be ruled out (at or above the ACS rule-out count)
        /// before a specimen can result as All Clinically Significant Antibodies Ruled Out.
        /// </summary>
        public static readonly IReadOnlyList<string> ClinicallySignificantAntigens = new[]
        {
            "D", "C", "c", "E", "e",
            "K", "k", "Fya", "Fyb", "Jka", "Jkb",
            "S", "s", "Lea", "Leb", "M", "N"
        };

        public const string AcsResultText = "All Clinically Significant Antibodies Ruled Out";
        public const double AcsProbabilityCutoff = 0.95;

        /// <summary>
        /// Low-frequency / non-standard antigens that are added to panels only as needed.
        /// </summary>
        public static readonly IReadOnlyList<WarehouseAntigenDefinition> WarehouseCatalog =
            new[]
            {
                new WarehouseAntigenDefinition { Name = "Doa", System = "Dombrock", Antithetical = "Dob", TreatmentNote = "Ficin unaffected; DTT destroyed" },
                new WarehouseAntigenDefinition { Name = "Dob", System = "Dombrock", Antithetical = "Doa", TreatmentNote = "Ficin unaffected; DTT destroyed" },
                new WarehouseAntigenDefinition { Name = "Dia", System = "Diego", Antithetical = "Dib", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Dib", System = "Diego", Antithetical = "Dia", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Wra", System = "Wright", Antithetical = "Wrb", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Wrb", System = "Wright", Antithetical = "Wra", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Coa", System = "Colton", Antithetical = "Cob", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Cob", System = "Colton", Antithetical = "Coa", TreatmentNote = "Ficin/DTT unaffected" },
                new WarehouseAntigenDefinition { Name = "Yta", System = "Cartwright", Antithetical = "Ytb", TreatmentNote = "Ficin unaffected; DTT destroyed" },
                new WarehouseAntigenDefinition { Name = "Ytb", System = "Cartwright", Antithetical = "Yta", TreatmentNote = "Ficin unaffected; DTT destroyed" },
                new WarehouseAntigenDefinition { Name = "Vel", System = "Vel", Antithetical = null, TreatmentNote = "Ficin enhanced; DTT unaffected" },
            };

        public static readonly IReadOnlyList<string> WarehouseAntigens =
            WarehouseCatalog.Select(d => d.Name).ToList();

        public static readonly IReadOnlyList<string> AllKnownAntigens =
            Antigens.Concat(WarehouseAntigens).ToList();

        private static readonly HashSet<string> StandardSet =
            new(Antigens, System.StringComparer.Ordinal);
        private static readonly HashSet<string> WarehouseSet =
            new(WarehouseAntigens, System.StringComparer.Ordinal);

        public static readonly IReadOnlyDictionary<string, string> AntitheticalPairs =
            BuildAntitheticalPairs();

        private static IReadOnlyDictionary<string, string> BuildAntitheticalPairs()
        {
            var pairs = new Dictionary<string, string>
            {
                { "E", "e" }, { "e", "E" },
                { "C", "c" }, { "c", "C" },
                { "K", "k" }, { "k", "K" },
                { "Jsa", "Jsb" }, { "Jsb", "Jsa" },
                { "Kpa", "Kpb" }, { "Kpb", "Kpa" },
                { "Jka", "Jkb" }, { "Jkb", "Jka" },
                { "Fya", "Fyb" }, { "Fyb", "Fya" },
                { "Lea", "Leb" }, { "Leb", "Lea" },
                { "M", "N" }, { "N", "M" },
                { "S", "s" }, { "s", "S" },
                { "Lua", "Lub" }, { "Lub", "Lua" }
            };
            foreach (var def in WarehouseCatalog)
            {
                if (string.IsNullOrEmpty(def.Antithetical)) continue;
                pairs[def.Name] = def.Antithetical;
            }
            return pairs;
        }

        public static bool IsStandard(string antigen) => StandardSet.Contains(antigen);
        public static bool IsWarehouse(string antigen) => WarehouseSet.Contains(antigen);
        public static bool IsKnown(string antigen) => IsStandard(antigen) || IsWarehouse(antigen);

        /// <summary>
        /// Standard antigens plus warehouse antigens that are typed on any of the
        /// specimen's run panels. Untyped warehouse antigens are omitted so they
        /// are not treated as antigen-negative.
        /// </summary>
        public static IReadOnlyList<string> GetAnalyzedAntigens(IEnumerable<string>? extraOnRunPanels)
        {
            if (extraOnRunPanels == null) return Antigens;
            var extraSet = extraOnRunPanels as HashSet<string>
                ?? new HashSet<string>(extraOnRunPanels, System.StringComparer.Ordinal);
            var extras = WarehouseAntigens.Where(extraSet.Contains).ToList();
            if (extras.Count == 0) return Antigens;
            return Antigens.Concat(extras).ToList();
        }

        public static readonly IReadOnlyList<string> ReactionValues =
            new[] { "0", "1+", "2+", "3+", "4+", "NT" };

        public static readonly IReadOnlyList<string> SpecimenTypes =
            new[] { "serum", "plasma", "eluate" };

        public static readonly IReadOnlyList<string> DatResults =
            new[] { "NT", "Negative", "W+", "1+", "2+", "3+", "4+" };

        public static readonly IReadOnlyList<string> AntigenValues =
            new[] { "+", "-" };

        public const string ZygosityBoth = "Both";
        public const string ZygosityHomozygous = "Homozygous";
        public const string ZygosityHeterozygous = "Heterozygous";

        /// <summary>
        /// Search-tab options for cells that are positive for a selected antigen.
        /// Homozygous = antigen+ and antithetical−; heterozygous = both +; both = any +.
        /// </summary>
        public static readonly IReadOnlyList<string> PositiveZygosityOptions =
            new[] { ZygosityBoth, ZygosityHomozygous, ZygosityHeterozygous };

        public const double ProbabilityThreshold = 0.5;
    }
}
