namespace AntibodyPanels.Models
{
    public class Rule
    {
        public int RuleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Antibody { get; set; } = string.Empty;
        public string? ExceptionAntigen { get; set; }
        public bool HeterozygousOk { get; set; }
        public int MinRuleoutCount { get; set; } = 3;
    }
}
