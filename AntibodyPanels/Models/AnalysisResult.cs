using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class AnalysisResult
    {
        public string SpecimenId { get; set; } = string.Empty;
        public Dictionary<string, int> RuledOut { get; set; } = new();
        public List<RuleoutEvaluation> RuleoutEvaluations { get; set; } = new();
        public Dictionary<string, double> Suspected { get; set; } = new();
        public Dictionary<string, SuspectedStatistics> SuspectedStatistics { get; set; } = new();
        public List<PatternMatch> PatternMatches { get; set; } = new();
        public Dictionary<string, List<RuleoutDetail>> DetailedRuleouts { get; set; } = new();
        public Dictionary<string, SuspectedEvidence> SuspectedEvidence { get; set; } = new();
        public List<AntibodyCombination> Combinations { get; set; } = new();
        public Dictionary<string, Dictionary<string, double>> PhraseProbabilities { get; set; } = new();
        public List<DosageEffect> DosageEffects { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public List<string> UntypedClinicallySignificant { get; set; } = new();
        public List<SelectedCellRecommendation> SelectedCellRecommendations { get; set; } = new();
        public List<PatientTypingConsideration> PatientTypingConsiderations { get; set; } = new();
        public bool PatientPhenotypeUnreliable { get; set; }
        public List<CandidateExplanation> CandidateExplanations { get; set; } = new();
        public List<string> SpecialReactionNotes { get; set; } = new();
        public List<ReactionPatternNote> ReactionPatterns { get; set; } = new();

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

        /// <summary>
        /// Whether the specimen can result as All Clinically Significant Antibodies Ruled Out.
        /// </summary>
        public AcsEvaluation Acs { get; set; } = new();
    }

    public class ReactionPatternNote
    {
        public string Kind { get; set; } = "";
        public int MatchingCells { get; set; }
        public int EvaluatedCells { get; set; }
        public string Explanation { get; set; } = "";
    }

    public class AcsExceptionAntibody
    {
        public string Antibody { get; set; } = string.Empty;
        public double CombinedScore { get; set; }
        public int RuleoutCount { get; set; }
    }

    public class AcsEvaluation
    {
        public bool IsEligible { get; set; }
        public bool IsEligibleWithException { get; set; }
        public int RequiredRuleoutCount { get; set; }
        public List<AcsShortfall> Shortfalls { get; set; } = new();
        public List<AcsExceptionAntibody> Exceptions { get; set; } = new();
        public string SuggestedCombinedResult { get; set; } = string.Empty;
        public string SuggestedComment { get; set; } = string.Empty;
    }

    public class AcsShortfall
    {
        public string Antibody { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Required { get; set; }
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

    /// <summary>
    /// Per-antibody rule-out evidence: observed qualifying cells versus the
    /// configured requirement. <see cref="MeetsCriteria"/> is analytical
    /// evidence, not a confirmed identification.
    /// </summary>
    public class RuleoutEvaluation
    {
        public string Antibody { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public int ObservedCount { get; set; }
        public int RequiredCount { get; set; }
        public int HomozygousCount { get; set; }
        public int HeterozygousCount { get; set; }
        public bool HeterozygousAllowed { get; set; }
        public bool MeetsCriteria { get; set; }
        public string PolicySource { get; set; } = "lab default";
        public string Explanation { get; set; } = string.Empty;
        public List<RuleoutDetail> Cells { get; set; } = new();
        public List<string> ConflictingReactiveCells { get; set; } = new();
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

    /// <summary>
    /// An unused inventory cell that may help discriminate remaining
    /// candidate antibodies. The score is analytical evidence, not a diagnosis.
    /// </summary>
    public enum PatientTypingKind
    {
        Supporting,
        Against,
        Uninterpretable,
        Historical,
        Predicted
    }

    public class CandidateExplanation
    {
        public string Antibody { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Narrative { get; set; } = string.Empty;
        public string RuleoutCriteria { get; set; } = string.Empty;
        public List<string> RuleoutCells { get; set; } = new();
        public List<string> SupportingCells { get; set; } = new();
        public List<string> ConflictingCells { get; set; } = new();
        public string DosageNote { get; set; } = string.Empty;
        public string PhaseNote { get; set; } = string.Empty;
        public string PhenotypeNote { get; set; } = string.Empty;
        public string AdditionalTesting { get; set; } = string.Empty;
        public string IdentificationNote { get; set; } = string.Empty;
    }

    public class PatientTypingConsideration
    {
        public string Antibody { get; set; } = string.Empty;
        public string Antigen { get; set; } = string.Empty;
        public PatientTypingKind Kind { get; set; }
        public string PatientValue { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
    }

    public class SelectedCellRecommendation
    {
        public int PanelId { get; set; }
        public string PanelName { get; set; } = string.Empty;
        public string? LotNumber { get; set; }
        public string CellNumber { get; set; } = string.Empty;
        public int Score { get; set; }
        public string Explanation { get; set; } = string.Empty;
        public string AntigenProfile { get; set; } = string.Empty;
        public List<string> Distinguishes { get; set; } = new();
        public bool IsExpired { get; set; }
        public bool IsExpiringSoon { get; set; }
        public string? ExpirationDate { get; set; }
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
