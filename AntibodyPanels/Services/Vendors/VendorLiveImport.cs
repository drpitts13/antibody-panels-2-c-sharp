using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AntibodyPanels.Data;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class VendorLiveImportResult
    {
        public string Vendor { get; init; } = "";
        public bool DownloadSupported { get; init; }
        public string? LotNumber { get; init; }
        public int? PanelId { get; init; }
        public string? Message { get; init; }
        public bool Persisted => PanelId.HasValue;
    }

    public static class VendorLiveImport
    {
        public static async Task<IReadOnlyList<VendorLiveImportResult>> ImportRandomLotPerVendorAsync(
            DatabaseService db, CancellationToken cancellationToken = default)
        {
            var results = new List<VendorLiveImportResult>();
            using var catalog = new VendorCatalogService();
            var importer = new VendorPanelImportService(db);
            var rng = new Random();

            foreach (var source in catalog.Sources)
            {
                if (!source.CanListLots)
                {
                    results.Add(new VendorLiveImportResult
                    {
                        Vendor = source.VendorId,
                        DownloadSupported = false,
                        Message = source.ListLotsUnavailableReason
                    });
                    continue;
                }

                try
                {
                    var lots = (await source.ListLotsAsync(cancellationToken).ConfigureAwait(false))
                        .Where(l => !string.IsNullOrWhiteSpace(l.DownloadUrl))
                        .ToList();
                    if (lots.Count == 0)
                    {
                        results.Add(new VendorLiveImportResult
                        {
                            Vendor = source.VendorId,
                            DownloadSupported = true,
                            Message = "Public catalog listed no downloadable lots."
                        });
                        continue;
                    }

                    var shuffled = lots.OrderBy(_ => rng.Next()).ToList();
                    VendorLiveImportResult? lastFail = null;
                    var imported = false;
                    foreach (var lot in shuffled.Take(Math.Min(6, shuffled.Count)))
                    {
                        var parsed = await catalog.DownloadAndParseAsync(lot, cancellationToken).ConfigureAwait(false);
                        if (!parsed.Success || parsed.Cells.Count < 8)
                        {
                            lastFail = new VendorLiveImportResult
                            {
                                Vendor = source.VendorId,
                                DownloadSupported = true,
                                LotNumber = lot.LotNumber,
                                Message = parsed.Success
                                    ? $"Downloaded {lot.LotNumber} but only parsed {parsed.Cells.Count} cells."
                                    : "Downloaded but parse failed: " + string.Join("; ", parsed.Errors)
                            };
                            continue;
                        }

                        var id = importer.Persist(parsed, replaceExisting: true);
                        results.Add(new VendorLiveImportResult
                        {
                            Vendor = source.VendorId,
                            DownloadSupported = true,
                            LotNumber = parsed.LotNumber ?? lot.LotNumber,
                            PanelId = id,
                            Message = $"Imported '{parsed.Name}' ({parsed.Cells.Count} cells)."
                        });
                        imported = true;
                        break;
                    }
                    if (!imported)
                        results.Add(lastFail ?? new VendorLiveImportResult
                        {
                            Vendor = source.VendorId,
                            DownloadSupported = true,
                            Message = "No listed lot produced a usable antigen table."
                        });
                }
                catch (Exception ex)
                {
                    results.Add(new VendorLiveImportResult
                    {
                        Vendor = source.VendorId,
                        DownloadSupported = true,
                        Message = ex.Message
                    });
                }
            }

            return results;
        }
    }
}
