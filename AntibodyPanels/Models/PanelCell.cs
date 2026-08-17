using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class PanelCell
    {
        public int Id { get; set; }
        public int PanelId { get; set; }
        public string CellNumber { get; set; } = string.Empty;

        // Antigen values keyed by antigen name (e.g. "D", "C", "c", ...)
        public Dictionary<string, string> Antigens { get; set; } = new();

        public string GetAntigen(string antigen) =>
            Antigens.TryGetValue(antigen, out var v) ? v : "-";

        /// <summary>
        /// True when this cell has a typed +/− value for the antigen.
        /// Warehouse antigens are only present after they have been added to the panel.
        /// Missing is not the same as antigen-negative.
        /// </summary>
        public bool HasTypedAntigen(string antigen) => Antigens.ContainsKey(antigen);

        public void SetAntigen(string antigen, string value) =>
            Antigens[antigen] = value;
    }
}
