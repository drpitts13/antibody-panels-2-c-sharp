namespace AntibodyPanels.Models
{
    public enum WorklistKind
    {
        IncompleteReactions,
        StaleAnalysis,
        ExpiringSpecimen,
        ExpiringPanel
    }

    public class WorklistItem
    {
        public WorklistKind Kind { get; set; }
        public string KindLabel { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string? AccessionNumber { get; set; }
        public int? PanelId { get; set; }
        public string TargetTab { get; set; } = "Specimens";
    }
}
