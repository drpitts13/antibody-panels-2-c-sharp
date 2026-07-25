using System;
using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    /// <summary>
    /// Represents one testing run of a reagent panel against a specific specimen.
    /// A specimen may have multiple runs of the same panel (untreated, ficin-treated,
    /// DTT-treated, absorbed, etc.), each producing an independent reaction set.
    /// </summary>
    public class PanelRun
    {
        public int RunId { get; set; }
        public string SpecimenId { get; set; } = string.Empty;
        public int PanelId { get; set; }
        public CellTreatment CellTreatment { get; set; } = CellTreatment.None;
        public SerumTreatment SerumTreatment { get; set; } = SerumTreatment.None;

        /// <summary>
        /// Optional free-text override label; if blank the display label is auto-generated.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        public string CreatedDate { get; set; } = string.Empty;

        // Navigation — populated by DatabaseService when needed
        public string PanelName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable label: uses <see cref="Label"/> when set, otherwise auto-generates
        /// from the cell / serum treatment combination.
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Label)) return Label;
                var parts = new List<string>();
                parts.Add(AntigenTreatmentEffects.GetDisplayName(CellTreatment));
                var serumLabel = AntigenTreatmentEffects.GetDisplayName(SerumTreatment);
                if (!string.IsNullOrEmpty(serumLabel)) parts.Add(serumLabel);
                return string.Join(" + ", parts);
            }
        }

        public bool IsUntreated =>
            CellTreatment == CellTreatment.None && SerumTreatment == SerumTreatment.None;

        public override string ToString() => DisplayLabel;
    }
}
