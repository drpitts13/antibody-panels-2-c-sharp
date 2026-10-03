using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class PanelImportReviewTests
{
    [Fact]
    public void CompareSchema_ReportsAddedAndRemovedAntigens()
    {
        var diff = PanelImportReviewer.CompareSchema(
            new[] { "D", "C", "c", "E", "e", "Lua" },
            new[] { "D", "C", "c", "E", "e", "Kpa" });

        Assert.True(diff.Changed);
        Assert.Contains("Lua", diff.Added);
        Assert.Contains("Kpa", diff.Removed);
        Assert.DoesNotContain("E", diff.Added);
        Assert.DoesNotContain("E", diff.Removed);
    }

    [Fact]
    public void Hash_IsStableForSameBytes()
    {
        var bytes = "Cell,D,C\n1,+,-"u8.ToArray();
        Assert.Equal(PanelImportReviewer.Hash(bytes), PanelImportReviewer.Hash(bytes));
        Assert.NotEqual(PanelImportReviewer.Hash(bytes), PanelImportReviewer.Hash("other"u8.ToArray()));
    }

    [Fact]
    public void Import_StoresInactiveWithArtifactAndNoPriorLotSentence()
    {
        using var iso = new IsolatedDatabase();
        var parsed = ParseCsv("""
            Cell,D,C,c,E,e
            1,+,-,-,-,+
            2,-,+,+,+,-
            """, "LOT-A", "10-cell");

        var outcome = new VendorPanelImportService(iso.Db).Import(parsed);
        var stored = iso.Db.GetPanel(outcome.PanelId);

        Assert.False(stored!.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(stored.SourceSha256));
        Assert.Equal(outcome.Review.Sha256, stored.SourceSha256);
        Assert.True(File.Exists(stored.SourceArtifactPath));
        Assert.Equal(parsed.SourceBytes, File.ReadAllBytes(stored.SourceArtifactPath!));
        Assert.Equal(PanelImportReviewer.Hash(parsed.SourceBytes!), stored.SourceSha256);
        Assert.Contains("stored inactive pending review", outcome.Review.Explanation);
        Assert.Contains("No prior lot", outcome.Review.Explanation);
        Assert.Contains(stored.SourceSha256, outcome.Review.Explanation);
        Assert.DoesNotContain(iso.Db.GetActivePanels(), p => p.PanelId == outcome.PanelId);
    }

    [Fact]
    public void Import_SecondLot_ExplainsAddedAndRemovedAntigens()
    {
        using var iso = new IsolatedDatabase();
        var first = ParseCsv("""
            Cell,D,C,c,E,e,Kpa
            1,+,-,-,-,+,+
            2,-,+,+,+,-,-
            """, "LOT-1", "10-cell");
        var firstId = new VendorPanelImportService(iso.Db).Persist(first);

        var second = ParseCsv("""
            Cell,D,C,c,E,e,Lua
            1,+,-,-,-,+,-
            2,-,+,+,+,-,+
            """, "LOT-2", "10-cell");
        var outcome = new VendorPanelImportService(iso.Db).Import(second);

        Assert.Equal("LOT-1", outcome.Review.PriorLotNumber);
        Assert.Equal(firstId, outcome.Review.PriorPanelId);
        Assert.Contains("Lua", outcome.Review.Schema.Added);
        Assert.Contains("Kpa", outcome.Review.Schema.Removed);
        Assert.Contains("differs from prior lot LOT-1", outcome.Review.Explanation);
        Assert.Contains("Added: Lua", outcome.Review.Explanation);
        Assert.Contains("Removed: Kpa", outcome.Review.Explanation);
        Assert.False(iso.Db.GetPanel(outcome.PanelId)!.IsActive);
    }

    [Fact]
    public void Import_MatchingSchema_SaysMatchesPriorLot()
    {
        using var iso = new IsolatedDatabase();
        var csv = """
            Cell,D,C,c,E,e
            1,+,-,-,-,+
            2,-,+,+,+,-
            """;
        new VendorPanelImportService(iso.Db).Persist(ParseCsv(csv, "LOT-1", "10-cell"));
        var outcome = new VendorPanelImportService(iso.Db).Import(ParseCsv(csv, "LOT-2", "10-cell"));

        Assert.False(outcome.Review.Schema.Changed);
        Assert.Equal("LOT-1", outcome.Review.PriorLotNumber);
        Assert.Contains("matches prior lot LOT-1", outcome.Review.Explanation);
    }

    [Fact]
    public void Activate_MakesPanelAvailableForInventory()
    {
        using var iso = new IsolatedDatabase();
        var id = new VendorPanelImportService(iso.Db).Persist(
            ParseCsv("Cell,D\n1,+\n", "LOT-ACT", null));
        Assert.Empty(iso.Db.GetActivePanels());

        iso.Db.SetPanelActive(id, true);
        Assert.Contains(iso.Db.GetActivePanels(), p => p.PanelId == id);
        Assert.True(iso.Db.GetPanel(id)!.IsActive);
    }

    private static VendorParseResult ParseCsv(string csv, string lot, string? productLine)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_review_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv);
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = lot;
            parsed.ProductLine = productLine;
            parsed.SourceFormat = "csv";
            return parsed;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
