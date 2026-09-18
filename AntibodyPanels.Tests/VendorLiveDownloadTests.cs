using AntibodyPanels.Data;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;
using Xunit.Abstractions;

namespace AntibodyPanels.Tests;

public class VendorLiveDownloadTests
{
    private readonly ITestOutputHelper _output;

    public VendorLiveDownloadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task LiveCatalog_DownloadsAndPersistsSupportedVendors()
    {
        using var iso = new IsolatedDatabase();
        var results = await VendorLiveImport.ImportRandomLotPerVendorAsync(iso.Db);
        foreach (var row in results)
            _output.WriteLine($"{row.Vendor}: supported={row.DownloadSupported} persisted={row.Persisted} lot={row.LotNumber} {row.Message}");

        Assert.Contains(results, r => r.Vendor == VendorIds.Immucor && !r.DownloadSupported);
        Assert.Contains(results, r => r.Vendor == VendorIds.Grifols && !r.DownloadSupported);
        Assert.Contains(results, r => r.Vendor == VendorIds.Medion && !r.DownloadSupported);

        var persisted = results.Where(r => r.Persisted).ToList();
        Assert.NotEmpty(persisted);
        Assert.Contains(persisted, r => r.Vendor == VendorIds.BioRad);

        foreach (var row in persisted)
        {
            var panel = iso.Db.GetPanel(row.PanelId!.Value);
            Assert.NotNull(panel);
            var cells = iso.Db.GetPanelCells(panel!.PanelId);
            Assert.True(cells.Count >= 8, $"{row.Vendor} imported fewer than 8 cells");
            Assert.Contains(cells, c => c.GetAntigen("D") == "+" || c.GetAntigen("D") == "-");
            Assert.False(string.IsNullOrWhiteSpace(panel.ImportedAt));
        }

        var livePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AntibodyPanels", "antibody_panels.db"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "AntibodyPanels", "bin", "Release", "net8.0-windows", "antibody_panels.db")),
        };

        foreach (var path in livePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var live = new DatabaseService(path);
            foreach (var row in persisted)
            {
                var source = iso.Db.GetPanel(row.PanelId!.Value)!;
                var parsed = new VendorParseResult
                {
                    Vendor = source.Vendor ?? row.Vendor,
                    Name = source.Name,
                    LotNumber = source.LotNumber,
                    ExpirationDate = source.ExpirationDate,
                    CatalogNumber = source.CatalogNumber,
                    ProductLine = source.ProductLine,
                    EnzymeTreated = source.EnzymeTreated,
                    SourceUrl = source.SourceUrl,
                    SourceFormat = source.SourceFormat ?? "pdf",
                    SpecialNotes = source.SpecialNotes
                };
                foreach (var cell in iso.Db.GetPanelCells(source.PanelId))
                    parsed.Cells.Add(cell);
                new VendorPanelImportService(live).Persist(parsed, replaceExisting: true);
                Assert.NotNull(live.FindPanelByVendorLot(parsed.Vendor, parsed.LotNumber));
            }
            _output.WriteLine($"Persisted {persisted.Count} vendor panel(s) to {path}");
        }
    }
}
