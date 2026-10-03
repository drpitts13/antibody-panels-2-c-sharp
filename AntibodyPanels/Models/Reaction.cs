namespace AntibodyPanels.Models
{
    public class Reaction
    {
        public int ReactionId { get; set; }

        /// <summary>Foreign key to panel_runs.run_id.</summary>
        public int RunId { get; set; }

        // Denormalized from the joined PanelRun row for convenient access in the analyzer.
        public string SpecimenId { get; set; } = string.Empty;
        public int PanelId { get; set; }
        public CellTreatment CellTreatment { get; set; } = CellTreatment.None;
        public SerumTreatment SerumTreatment { get; set; } = SerumTreatment.None;

        public string CellNumber { get; set; } = string.Empty;
        public string IS { get; set; } = "NT";
        public string C37 { get; set; } = "NT";
        public string AHG { get; set; } = "NT";
        public string CC { get; set; } = "NT";

        /// <summary>
        /// True when all interpretable phases (IS, 37°C, AHG) are non-reactive.
        /// CC is a check-cell validity control and is excluded from reactivity logic.
        /// </summary>
        public bool IsNegative =>
            ReactionGrade.IsNegative(AHG) && ReactionGrade.IsAbsent(IS) && ReactionGrade.IsAbsent(C37);

        /// <summary>
        /// True when at least one interpretable phase (IS, 37°C, AHG) shows reactivity.
        /// w+, MF, and hemolysis count as reactive.
        /// </summary>
        public bool IsPositive =>
            ReactionGrade.IsPositive(IS) || ReactionGrade.IsPositive(C37) || ReactionGrade.IsPositive(AHG);
    }
}
