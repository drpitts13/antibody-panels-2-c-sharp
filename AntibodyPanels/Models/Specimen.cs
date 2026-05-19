using System;
using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class Specimen
    {
        public string AccessionNumber { get; set; } = string.Empty;
        public string Type { get; set; } = "serum";
        public string? ExpirationDate { get; set; }
        public string CreatedDate { get; set; } = string.Empty;
        public string? ReactionsUpdatedAt { get; set; }
        public string? LastAnalyzedAt { get; set; }
        public bool IsActive { get; set; } = true;

        public List<SpecimenAntibody> Antibodies { get; set; } = new();
        public List<SpecimenRuleout> Ruleouts { get; set; } = new();
        public List<Panel> LinkedPanels { get; set; } = new();

        public bool IsAnalysisStale =>
            ReactionsUpdatedAt != null &&
            (LastAnalyzedAt == null || string.Compare(ReactionsUpdatedAt, LastAnalyzedAt) > 0);
    }
}
