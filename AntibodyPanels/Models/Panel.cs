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
    }
}
