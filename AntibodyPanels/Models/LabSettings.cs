using System;

namespace AntibodyPanels.Models
{
    public class LabSettings
    {
        public string LabName { get; set; } = "Immunohematology Laboratory";
        public string Department { get; set; } = "";
        public double ProbabilityThreshold { get; set; } = 0.5;
        public string DefaultSpecimenType { get; set; } = "serum";
        public bool ShowInactiveByDefault { get; set; }
        public bool HideRuledOutAntigenColumns { get; set; }
        public int ExpirationWarningDays { get; set; } = 14;

        public static LabSettings CreateDefault() => new();

        public void Clamp()
        {
            if (ProbabilityThreshold < 0.3) ProbabilityThreshold = 0.3;
            if (ProbabilityThreshold > 0.95) ProbabilityThreshold = 0.95;
            if (ExpirationWarningDays < 1) ExpirationWarningDays = 1;
            if (ExpirationWarningDays > 90) ExpirationWarningDays = 90;
            if (string.IsNullOrWhiteSpace(LabName))
                LabName = "Immunohematology Laboratory";
            if (string.IsNullOrWhiteSpace(DefaultSpecimenType) ||
                Array.IndexOf(new[] { "serum", "plasma", "eluate" }, DefaultSpecimenType) < 0)
                DefaultSpecimenType = "serum";
        }
    }
}
