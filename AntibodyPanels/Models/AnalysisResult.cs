using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class AnalysisResult
    {
        public string SpecimenId { get; set; } = string.Empty;
        public Dictionary<string, int> RuledOut { get; set; } = new();
        public Dictionary<string, double> Suspected { get; set; } = new();
        public Dictionary<string, SuspectedStatistics> SuspectedStatistics { get; set; } = new();
        public List<PatternMatch> PatternMatches { get; set; } = new();
        public Dictionary<string, List<RuleoutDetail>> DetailedRuleouts { get; set; } = new();
        public Dictionary<string, SuspectedEvidence> SuspectedEvidence { get; set; } = new();
        public List<AntibodyCombination> Combinations { get; set; } = new();
        public Dictionary<string, Dictionary<string, double>> PhraseProbabilities { get; set; } = new();
        public List<DosageEffect> DosageEffects { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();

        // ── Special-panel inference outputs ───────────────────────────────────

        /// <summary>
        /// Antibodies whose rule-outs were suppressed because the relevant antigen
        /// was destroyed on the treated cells used to generate the negative reactions.
        /// e.g. anti-Fya cannot be ruled out from ficin-treated cells.
        /// </summary>
        public List<GatedRuleout> GatedRuleouts { get; set; } = new();

        /// <summary>
        /// Clinical inferences drawn by comparing treated vs untreated runs,
        /// e.g. "Reactivity lost on ficin cells → Fya system suspect".
        /// </summary>
        public List<TreatmentInference> TreatmentInferences { get; set; } = new();

        /// <summary>
        /// Conclusions about which antibodies survived each allogeneic absorption step.
        /// </summary>
        public List<AbsorptionConclusion> AbsorptionConclusions { get; set; } = new();
    }

    /// <summary>
    /// An antibody rule-out that could not be counted because the relevant
    /// antigen was destroyed by the cell treatment on the reacting run.
    /// </summary>
    public class GatedRuleout
    {
        public string Antibody { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public string CellTreatmentLabel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// A clinical interpretation derived from the difference in reactivity
    /// between a treated and the corresponding untreated run.
    /// </summary>
    public class TreatmentInference
    {
        public string RunLabel { get; set; } = string.Empty;
        public string Antibody { get; set; } = string.Empty;
        public string Observation { get; set; } = string.Empty;
        public TreatmentInferenceType InferenceType { get; set; }
    }

    public enum TreatmentInferenceType
    {
        ReactivityLostOnEnzyme,    // supports IgM or antigen destroyed by enzyme
        ReactivityGainedOnEnzyme,  // antigen enhanced by enzyme
        ReactivityLostOnDTT,       // supports Kell/Lutheran system
        ReactivitySurvivedAbsorption,  // antibody not removed by absorbing cells
        ReactivityRemovedByAbsorption, // antibody removed by absorbing cells
    }

    /// <summary>
    /// Summary of which antibodies survived or were absorbed out in a
    /// differential absorption aliquot.
    /// </summary>
    public class AbsorptionConclusion
    {
        public string AbsorptionLabel { get; set; } = string.Empty;
        public List<string> AbsorbedOut { get; set; } = new();
        public List<string> Surviving { get; set; } = new();
    }

    public class SuspectedStatistics
    {
        public double FisherPValue { get; set; }
        public double PatternScore { get; set; }
        public double FisherComponent { get; set; }
        public double CombinedScore { get; set; }
        public int PositiveAgPositiveCount { get; set; }
        public int NegativeAgNegativeCount { get; set; }
        public int IdentificationRequired { get; set; }
        public bool MeetsIdentificationRule { get; set; }

        public string IdentificationRuleLabel =>
            $"{IdentificationRequired} + {IdentificationRequired}";

        public string IdentificationStatus =>
            MeetsIdentificationRule ? $"Meets {IdentificationRuleLabel}" : "Incomplete";

        public string IdentificationDetail =>
            $"{IdentificationStatus} ({PositiveAgPositiveCount}/{IdentificationRequired} Ag+ reactive, " +
            $"{NegativeAgNegativeCount}/{IdentificationRequired} Ag- nonreactive)";
    }

    public class PatternMatch
    {
        public string Antibody { get; set; } = string.Empty;
        public int Matches { get; set; }
        public int Mismatches { get; set; }
        public double Confidence { get; set; }
    }

    public class RuleoutDetail
    {
        public int RunId { get; set; }
        public string RunLabel { get; set; } = string.Empty;
        public int PanelId { get; set; }
        public string PanelName { get; set; } = string.Empty;
        public string CellNumber { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public string AntigenValue { get; set; } = string.Empty;
        public string? Antithetical { get; set; }
        public string? AntitheticalValue { get; set; }
        public bool IsHomozygous { get; set; }
        public string IS { get; set; } = "NT";
        public string C37 { get; set; } = "NT";
        public string AHG { get; set; } = "NT";
        public string CC { get; set; } = "NT";
    }

    public class SuspectedEvidence
    {
        public double Probability { get; set; }
        public List<EvidenceCell> SupportingCells { get; set; } = new();
        public List<EvidenceCell> ConflictingCells { get; set; } = new();
        public double PatternQuality { get; set; }
        public int TotalSupporting { get; set; }
        public int TotalConflicting { get; set; }
    }

    public class EvidenceCell
    {
        public int RunId { get; set; }
        public string RunLabel { get; set; } = string.Empty;
        public int PanelId { get; set; }
        public string PanelName { get; set; } = string.Empty;
        public string CellNumber { get; set; } = string.Empty;
        public string IS { get; set; } = "NT";
        public string C37 { get; set; } = "NT";
        public string AHG { get; set; } = "NT";
        public string CC { get; set; } = "NT";
        public string StrongestPhase { get; set; } = string.Empty;
        public string StrongestValue { get; set; } = "0";
    }

    public class AntibodyCombination
    {
        public List<string> Antibodies { get; set; } = new();
        public List<double> Probabilities { get; set; } = new();
        public int BothSupport { get; set; }
        public int Ab1Only { get; set; }
        public int Ab2Only { get; set; }
        public int Neither { get; set; }
        public double CombinationScore { get; set; }
    }

    public class DosageEffect
    {
        public string Antibody { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public double AvgHomozygous { get; set; }
        public double AvgHeterozygous { get; set; }
        public int HomozygousCount { get; set; }
        public int HeterozygousCount { get; set; }
        public string Severity { get; set; } = "medium";
    }
}
