namespace AntibodyPanels.Models
{
    public class AuditEvent
    {
        public long Id { get; set; }
        public string OccurredAtUtc { get; set; } = "";
        public string Operator { get; set; } = "";
        public string Action { get; set; } = "";
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string? Reason { get; set; }
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
    }
}
