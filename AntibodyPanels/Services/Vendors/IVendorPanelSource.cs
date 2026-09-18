using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public interface IVendorPanelSource
    {
        string VendorId { get; }
        string DisplayName { get; }
        bool CanListLots { get; }
        string? ListLotsUnavailableReason { get; }

        Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(CancellationToken cancellationToken = default);
        Task<byte[]> DownloadAsync(VendorLotListing lot, CancellationToken cancellationToken = default);
        VendorParseResult Parse(Stream stream, string? fileNameHint, VendorLotListing? listing);
    }
}
