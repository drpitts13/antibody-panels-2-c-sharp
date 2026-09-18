namespace AntibodyPanels.Services.Vendors
{
    public sealed class GrifolsMedionFileSource : VendorPanelSourceBase
    {
        public GrifolsMedionFileSource(VendorHttpClient http, string vendorId, string displayName)
            : base(http)
        {
            VendorId = vendorId;
            DisplayName = displayName;
        }

        public override string VendorId { get; }
        public override string DisplayName { get; }
        public override bool CanListLots => false;
        public override string? ListLotsUnavailableReason =>
            "Grifols / Medion antigen matrices ship with the kit or upload into Grifols middleware. Import the PDF or CSV from the product insert or customer portal.";
    }
}
