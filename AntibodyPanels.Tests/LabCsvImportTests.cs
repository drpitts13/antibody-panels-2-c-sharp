using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class LabCsvImportTests
{
    [Fact]
    public void SubsetSheet_DoesNotTypeMissingAntigensAsNegative()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteCsv("""
            Cell,D,C,c,E,e
            1,+,-,-,-,+
            2,-,+,+,+,-
            """);
        try
        {
            var parsed = PanelCsvService.Import(path);
            Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
            Assert.False(parsed.Cells[0].Antigens.ContainsKey("Fya"));
            Assert.False(parsed.Cells[0].Antigens.ContainsKey("K"));
            Assert.Equal("+", parsed.Cells[0].Antigens["D"]);

            var outcome = new LabPanelImportService(iso.Db).Import(parsed, Request("LOT-SUB", "Lab"));
            var cells = iso.Db.GetPanelCells(outcome.PanelId);
            Assert.False(cells[0].HasTypedAntigen("Fya"));
            Assert.False(cells[0].HasTypedAntigen("K"));
            Assert.True(cells[0].HasTypedAntigen("D"));
            Assert.Equal(new[] { "D", "C", "c", "E", "e" }, iso.Db.GetPanelTypedAntigens(outcome.PanelId));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Import_StoresInactiveArtifact_AndExplainsMissingPriorLot()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteCsv("""
            Cell,D,C,c
            1,+,-,-
            """);
        try
        {
            var parsed = PanelCsvService.Import(path);
            var outcome = new LabPanelImportService(iso.Db).Import(parsed, Request("LOT-CSV-1", "Local lab"));
            var stored = iso.Db.GetPanel(outcome.PanelId);
            Assert.False(stored!.IsActive);
            Assert.Equal(PanelImportReviewer.Hash(parsed.SourceBytes!), stored.SourceSha256);
            Assert.True(File.Exists(stored.SourceArtifactPath));
            Assert.Equal(parsed.SourceBytes, File.ReadAllBytes(stored.SourceArtifactPath!));
            Assert.Contains("stored inactive pending review", outcome.Review.Explanation);
            Assert.Contains("No prior lot", outcome.Review.Explanation);
            Assert.Contains(stored.SourceSha256!, outcome.Review.Explanation);
            Assert.DoesNotContain(iso.Db.GetActivePanels(), p => p.PanelId == outcome.PanelId);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void SecondLot_ExplainsSchemaChangeVersusPrior()
    {
        using var iso = new IsolatedDatabase();
        var firstPath = WriteCsv("""
            Cell,D,C,c,E,e,Kpa
            1,+,-,-,-,+,+
            """);
        var secondPath = WriteCsv("""
            Cell,D,C,c,E,e,Lua
            1,+,-,-,-,+,-
            """);
        try
        {
            var first = new LabPanelImportService(iso.Db).Import(
                PanelCsvService.Import(firstPath), Request("CSV-1", "Local lab", "Screen"));
            var outcome = new LabPanelImportService(iso.Db).Import(
                PanelCsvService.Import(secondPath), Request("CSV-2", "Local lab", "Screen"));

            Assert.Equal("CSV-1", outcome.Review.PriorLotNumber);
            Assert.Equal(first.PanelId, outcome.Review.PriorPanelId);
            Assert.Contains("Lua", outcome.Review.Schema.Added);
            Assert.Contains("Kpa", outcome.Review.Schema.Removed);
            Assert.Contains("differs from prior lot CSV-1", outcome.Review.Explanation);
            Assert.Contains("Added: Lua", outcome.Review.Explanation);
            Assert.Contains("Removed: Kpa", outcome.Review.Explanation);
        }
        finally
        {
            TryDelete(firstPath);
            TryDelete(secondPath);
        }
    }

    [Fact]
    public void DuplicateVendorLot_IsRejectedUntilReplace()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteCsv("Cell,D\n1,+\n");
        try
        {
            var importer = new LabPanelImportService(iso.Db);
            importer.Import(PanelCsvService.Import(path), Request("DUP-1", "Local lab"));
            Assert.Throws<InvalidOperationException>(() =>
                importer.Import(PanelCsvService.Import(path), Request("DUP-1", "Local lab")));
            var replaced = importer.Import(PanelCsvService.Import(path), Request("DUP-1", "Local lab"),
                replaceExisting: true);
            Assert.False(iso.Db.GetPanel(replaced.PanelId)!.IsActive);
        }
        finally { TryDelete(path); }
    }

    private static LabPanelImportRequest Request(string lot, string vendor, string? productLine = null) =>
        new()
        {
            Name = "Lab CSV " + lot,
            LotNumber = lot,
            Vendor = vendor,
            ProductLine = productLine,
            NumCells = 1,
            StartCell = 1,
        };

    private static string WriteCsv(string csv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lab_csv_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
