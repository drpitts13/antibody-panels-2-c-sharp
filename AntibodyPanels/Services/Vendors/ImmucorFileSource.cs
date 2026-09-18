namespace AntibodyPanels.Services.Vendors
{
    public sealed class ImmucorFileSource : VendorPanelSourceBase
    {
        public ImmucorFileSource(VendorHttpClient http) : base(http) { }

        public override string VendorId => VendorIds.Immucor;
        public override string DisplayName => "Immucor / Werfen";
        public override bool CanListLots => false;
        public override string? ListLotsUnavailableReason =>
            "Immucor Panocell Master Lists are published in the Werfen customer center and ImmuLINK, not on a public catalog. Import the PDF or CSV you download there.";
    }
}
