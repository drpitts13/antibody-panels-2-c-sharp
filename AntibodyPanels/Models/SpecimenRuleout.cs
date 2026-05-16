namespace AntibodyPanels.Models
{
    public class SpecimenRuleout
    {
        public int Id { get; set; }
        public string SpecimenId { get; set; } = string.Empty;
        public string Antibody { get; set; } = string.Empty;
        public int RuleoutCount { get; set; }
    }
}
