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

        public void SetAntigen(string antigen, string value) =>
            Antigens[antigen] = value;
    }
}
