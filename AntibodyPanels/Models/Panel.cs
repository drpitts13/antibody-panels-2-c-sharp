using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class Panel
    {
        public int PanelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LotNumber { get; set; }
        public string? Vendor { get; set; }
        public int NumCells { get; set; }
        public int StartCell { get; set; } = 1;
        public string? ExpirationDate { get; set; }
        public bool IncludeAc { get; set; }
        public bool IsActive { get; set; } = true;

        public List<PanelCell> Cells { get; set; } = new();

        public override string ToString() => Name;

        public string ListDisplay => FormatListDisplay(Name, LotNumber, ExpirationDate);

        public static string FormatListDisplay(string? name, string? lotNumber, string? expirationDate)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name.Trim());
            if (!string.IsNullOrWhiteSpace(lotNumber)) parts.Add(lotNumber.Trim());
            if (!string.IsNullOrWhiteSpace(expirationDate)) parts.Add($"Exp {expirationDate.Trim()}");
            return string.Join("  ·  ", parts);
        }

        public bool MatchesFilter(string? query) =>
            TextFilter.Matches(query, Name, LotNumber, Vendor, ExpirationDate);
    }
}
