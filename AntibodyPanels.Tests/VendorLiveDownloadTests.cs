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

    [Fact]
    public async Task PublicPdf_ImportFile_ParsesKnownVendorAntigrams()
    {
        using var http = new VendorHttpClient();
        using var catalog = new VendorCatalogService();
        using var iso = new IsolatedDatabase();
        var importer = new VendorPanelImportService(iso.Db);
        var dir = Path.Combine(Path.GetTempPath(), "AntibodyPanelsVendorImport");
        Directory.CreateDirectory(dir);

        var files = new (string Vendor, string Url, string FileName)[]
        {
            (VendorIds.BioRad,
                "https://backend.ih-area.bio-rad.com/system/files/F06171.31X_V.01.pdf",
                "F06171.31X_V.01.pdf"),
            (VendorIds.Quotient,
                "https://alivedx.com/wp-content/uploads/2023/10/V265925.pdf",
                "V265925.pdf"),
            (VendorIds.Quotient,
                "https://alivedx.com/wp-content/uploads/2023/10/V265842_V265844.pdf",
                "V265842_V265844.pdf"),
        };

        foreach (var (vendor, url, fileName) in files)
        {
            var path = Path.Combine(dir, fileName);
            var bytes = await http.GetBytesAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
            Assert.True(bytes.Length > 1000, $"{fileName} download was too small ({bytes.Length} bytes)");

            var parsed = catalog.ImportFile(vendor, path);
            parsed.SourceUrl ??= url;
            parsed.SourceFormat = "pdf";
            _output.WriteLine(
                $"{vendor} {fileName}: success={parsed.Success} cells={parsed.Cells.Count} " +
                $"lot={parsed.LotNumber} errors={string.Join("; ", parsed.Errors)}");
            Assert.True(parsed.Success, $"{fileName}: " + string.Join("\n", parsed.Errors));
            Assert.False(string.IsNullOrWhiteSpace(parsed.LotNumber), $"{fileName} parsed cells but no lot number");
            Assert.True(parsed.Cells.Count >= 8, $"{fileName} parsed {parsed.Cells.Count} cells");
            Assert.Contains(parsed.Cells, c => c.GetAntigen("D") == "+" || c.GetAntigen("D") == "-");

            var id = importer.Persist(parsed, replaceExisting: true);
            Assert.NotNull(iso.Db.FindPanelByVendorLot(parsed.Vendor, parsed.LotNumber));
            _output.WriteLine($"Persisted {vendor} lot {parsed.LotNumber} as panel #{id} from {path}");
        }
    }
}
