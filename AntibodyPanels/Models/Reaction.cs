namespace AntibodyPanels.Models
{
    public class Reaction
    {
        public int ReactionId { get; set; }
        public string SpecimenId { get; set; } = string.Empty;
        public int PanelId { get; set; }
        public string CellNumber { get; set; } = string.Empty;
        public string IS { get; set; } = "NT";
        public string C37 { get; set; } = "NT";
        public string AHG { get; set; } = "NT";
        public string CC { get; set; } = "NT";

        public bool IsNegative =>
            (IS == "0" && C37 == "0" && AHG == "0" && CC == "0") ||
            (AHG == "0" && IsNtOrZero(IS) && IsNtOrZero(C37) && IsNtOrZero(CC));

        public bool IsPositive =>
            IsReactionStrong(IS) || IsReactionStrong(C37) ||
            IsReactionStrong(AHG) || IsReactionStrong(CC);

        private static bool IsNtOrZero(string v) => v == "NT" || v == "0";
        private static bool IsReactionStrong(string v) => v != "NT" && v != "0" && !string.IsNullOrEmpty(v);
    }
}
