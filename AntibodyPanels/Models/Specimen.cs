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

        public string? Notes { get; set; }
        public string? Phenotype { get; set; }
        public string? PreviousAntibodies { get; set; }
        public string? DatResult { get; set; }

        public string? FinalAntibodies { get; set; }
        public string? FinalComment { get; set; }
        public string? IdentifiedBy { get; set; }
        public string? IdentifiedAt { get; set; }
        public bool HasFinalCall => !string.IsNullOrWhiteSpace(FinalAntibodies);
        public string FinalIdDisplay => HasFinalCall ? FinalAntibodies! : "";

        public List<SpecimenAntibody> Antibodies { get; set; } = new();
        public List<SpecimenRuleout> Ruleouts { get; set; } = new();
        public List<Panel> LinkedPanels { get; set; } = new();

        public bool IsAnalysisStale =>
            ReactionsUpdatedAt != null &&
            (LastAnalyzedAt == null || string.Compare(ReactionsUpdatedAt, LastAnalyzedAt) > 0);

        public bool MatchesFilter(string? query) =>
            TextFilter.Matches(query, AccessionNumber, Type, Phenotype, PreviousAntibodies,
                Notes, FinalAntibodies, DatResult, ExpirationDate);
    }
}
