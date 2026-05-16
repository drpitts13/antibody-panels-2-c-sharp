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
    }

    public class SuspectedStatistics
    {
        public double FisherPValue { get; set; }
        public double PatternScore { get; set; }
        public double FisherComponent { get; set; }
        public double CombinedScore { get; set; }
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
