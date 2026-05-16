using System.Collections.Generic;

namespace AntibodyPanels.Data
{
    /// <summary>
    /// Maps antigen display names to SQL-safe column names (mirrors antigen_mapper.py).
    /// </summary>
    public static class AntigenMapper
    {
        private static readonly Dictionary<string, string> AntigenToColumn = new()
        {
            { "D",   "ag_D" },
            { "C",   "ag_C_upper" },
            { "c",   "ag_c_lower" },
            { "E",   "ag_E_upper" },
            { "e",   "ag_e_lower" },
            { "f",   "ag_f" },
            { "Cw",  "ag_Cw" },
            { "V",   "ag_V" },
            { "K",   "ag_K_upper" },
            { "k",   "ag_k_lower" },
            { "Kpa", "ag_Kpa" },
            { "Kpb", "ag_Kpb" },
            { "Jsa", "ag_Jsa" },
            { "Jsb", "ag_Jsb" },
            { "Jka", "ag_Jka" },
            { "Jkb", "ag_Jkb" },
            { "Fya", "ag_Fya" },
            { "Fyb", "ag_Fyb" },
            { "Lea", "ag_Lea" },
            { "Leb", "ag_Leb" },
            { "M",   "ag_M_upper" },
            { "N",   "ag_N_upper" },
            { "S",   "ag_S_upper" },
            { "s",   "ag_s_lower" },
            { "Lua", "ag_Lua" },
            { "Lub", "ag_Lub" },
            { "Xga", "ag_Xga" },
            { "P1",  "ag_P1" }
        };

        private static readonly Dictionary<string, string> ColumnToAntigen;

        static AntigenMapper()
        {
            ColumnToAntigen = new Dictionary<string, string>();
            foreach (var kvp in AntigenToColumn)
                ColumnToAntigen[kvp.Value] = kvp.Key;
        }

        public static string GetColumn(string antigen) =>
            AntigenToColumn.TryGetValue(antigen, out var col) ? col : $"ag_{antigen}";

        public static string? GetAntigen(string column) =>
            ColumnToAntigen.TryGetValue(column, out var ag) ? ag : null;

        public static IEnumerable<string> AllColumns => AntigenToColumn.Values;
    }
}
