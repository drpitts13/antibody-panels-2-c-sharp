using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class VendorCatalogService : IDisposable
    {
        private readonly VendorHttpClient _http;
        private readonly IReadOnlyList<IVendorPanelSource> _sources;

        public VendorCatalogService(VendorHttpClient? http = null)
        {
            _http = http ?? new VendorHttpClient();
            _sources = new IVendorPanelSource[]
            {
                new BioRadIhAreaSource(_http),
                new ImmucorFileSource(_http),
                new OrthoEAntigramSource(_http),
                new GrifolsMedionFileSource(_http, VendorIds.Grifols, "Grifols"),
                new GrifolsMedionFileSource(_http, VendorIds.Medion, "Medion"),
                new QuotientAliveDxSource(_http),
            };
        }

        public IReadOnlyList<IVendorPanelSource> Sources => _sources;

        public IVendorPanelSource GetSource(string vendorId) =>
            _sources.FirstOrDefault(s =>
                string.Equals(s.VendorId, vendorId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.DisplayName, vendorId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown vendor: " + vendorId, nameof(vendorId));

        public async Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(string vendorId,
            CancellationToken cancellationToken = default)
        {
            var source = GetSource(vendorId);
            if (!source.CanListLots)
                return Array.Empty<VendorLotListing>();
            return await source.ListLotsAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<VendorParseResult> DownloadAndParseAsync(VendorLotListing lot,
            CancellationToken cancellationToken = default)
        {
            var source = GetSource(lot.Vendor);
            var bytes = await source.DownloadAsync(lot, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes, writable: false);
            var parsed = source.Parse(stream, lot.LotNumber + "." + lot.SourceFormat, lot);
            parsed.SourceUrl ??= lot.DownloadUrl;
            parsed.SourceFormat = lot.SourceFormat;
            return parsed;
        }

        public VendorParseResult ImportFile(string vendorId, string filePath)
        {
            var source = GetSource(vendorId);
            using var stream = File.OpenRead(filePath);
            return source.Parse(stream, Path.GetFileName(filePath), null);
        }

        public void Dispose() => _http.Dispose();
    }
}
