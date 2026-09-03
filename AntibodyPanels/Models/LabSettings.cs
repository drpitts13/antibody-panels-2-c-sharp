using System;

namespace AntibodyPanels.Models
{
    public class LabSettings
    {
        public string LabName { get; set; } = "Immunohematology Laboratory";
        public string Department { get; set; } = "";
        public double ProbabilityThreshold { get; set; } = 0.5;
        public int IdentificationCellCount { get; set; } = 3;
        public string DefaultSpecimenType { get; set; } = "serum";
        public bool ShowInactiveByDefault { get; set; }
        public bool HideRuledOutAntigenColumns { get; set; }
        public int ExpirationWarningDays { get; set; } = 14;
        public int MaxDatabaseSizeMb { get; set; } = 500;
        public bool WorklistShowIncomplete { get; set; } = true;
        public bool WorklistShowStale { get; set; } = true;
        public bool WorklistShowExpiring { get; set; } = true;
        public bool WorklistShowExpired { get; set; } = true;

        public string IdentificationRuleLabel =>
            $"{IdentificationCellCount} + {IdentificationCellCount}";

        public static LabSettings CreateDefault() => new();

        public void Clamp()
        {
            if (ProbabilityThreshold < 0.3) ProbabilityThreshold = 0.3;
            if (ProbabilityThreshold > 0.95) ProbabilityThreshold = 0.95;
            if (IdentificationCellCount < 1 || IdentificationCellCount > 3)
                IdentificationCellCount = 3;
            if (ExpirationWarningDays < 1) ExpirationWarningDays = 1;
            if (ExpirationWarningDays > 90) ExpirationWarningDays = 90;
            if (MaxDatabaseSizeMb < 50) MaxDatabaseSizeMb = 50;
            if (MaxDatabaseSizeMb > 10240) MaxDatabaseSizeMb = 10240;
            if (string.IsNullOrWhiteSpace(LabName))
                LabName = "Immunohematology Laboratory";
            if (string.IsNullOrWhiteSpace(DefaultSpecimenType) ||
                Array.IndexOf(new[] { "serum", "plasma", "eluate" }, DefaultSpecimenType) < 0)
                DefaultSpecimenType = "serum";
        }

        public bool ShowsWorklistKind(WorklistKind kind) => kind switch
        {
            WorklistKind.IncompleteReactions => WorklistShowIncomplete,
            WorklistKind.StaleAnalysis => WorklistShowStale,
            WorklistKind.ExpiringSpecimen or WorklistKind.ExpiringPanel => WorklistShowExpiring,
            WorklistKind.ExpiredSpecimen or WorklistKind.ExpiredPanel => WorklistShowExpired,
            _ => true
        };
    }
}
