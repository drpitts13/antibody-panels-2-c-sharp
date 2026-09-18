namespace AntibodyPanels.Models
{
    public class AnalysisSnapshot
    {
        public long Id { get; set; }
        public string SpecimenId { get; set; } = "";
        public string AnalyzedAtUtc { get; set; } = "";
        public string SoftwareVersion { get; set; } = "";
        public string SettingsJson { get; set; } = "";
        public string InputFingerprint { get; set; } = "";
        public string? RuledOutJson { get; set; }
        public string? SuspectedJson { get; set; }
        public string? AcsJson { get; set; }
    }
}
