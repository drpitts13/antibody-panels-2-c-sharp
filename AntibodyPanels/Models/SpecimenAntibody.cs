namespace AntibodyPanels.Models
{
    public class SpecimenAntibody
    {
        public int Id { get; set; }
        public string SpecimenId { get; set; } = string.Empty;
        public string Antibody { get; set; } = string.Empty;
        public double Probability { get; set; }

        public string ProbabilityDisplay => $"{Probability * 100:F1}%";
    }
}
