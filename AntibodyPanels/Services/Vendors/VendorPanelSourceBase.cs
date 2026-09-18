using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public abstract class VendorPanelSourceBase : IVendorPanelSource
    {
        protected VendorPanelSourceBase(VendorHttpClient http)
        {
            Http = http;
        }

        protected VendorHttpClient Http { get; }

        public abstract string VendorId { get; }
        public abstract string DisplayName { get; }
        public virtual bool CanListLots => false;
        public virtual string? ListLotsUnavailableReason =>
            CanListLots ? null : "Lot-specific files are not published on a public catalog. Import a PDF or CSV downloaded from the vendor portal.";

        public virtual Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VendorLotListing>>(Array.Empty<VendorLotListing>());

        public virtual async Task<byte[]> DownloadAsync(VendorLotListing lot,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lot.DownloadUrl))
                throw new InvalidOperationException($"{VendorId} lot {lot.LotNumber} has no public download URL.");
            return await Http.GetBytesAsync(lot.DownloadUrl, cancellationToken).ConfigureAwait(false);
        }

        public virtual VendorParseResult Parse(Stream stream, string? fileNameHint, VendorLotListing? listing) =>
            VendorAntigramParser.Parse(stream, VendorId, fileNameHint, listing);
    }
}
