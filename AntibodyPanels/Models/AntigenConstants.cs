using System.Collections.Generic;

namespace AntibodyPanels.Models
{
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

        public static readonly IReadOnlyDictionary<string, string> AntitheticalPairs =
            new Dictionary<string, string>
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

        public static readonly IReadOnlyList<string> ReactionValues =
            new[] { "0", "1+", "2+", "3+", "4+", "NT" };

        public static readonly IReadOnlyList<string> SpecimenTypes =
            new[] { "serum", "plasma", "eluate" };

        public static readonly IReadOnlyList<string> AntigenValues =
            new[] { "+", "-" };

        public const double ProbabilityThreshold = 0.8;
    }
}
