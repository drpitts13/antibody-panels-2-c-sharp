using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ImportArtifactViewerTests
{
    [Fact]
    public void Inspect_MatchingFile_IsPresentAndPreviewsCsv()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ab_art_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bytes = "Cell,D,C\n1,+,-"u8.ToArray();
            var sha = PanelImportReviewer.Hash(bytes);
            var path = Path.Combine(dir, "lot.csv");
            File.WriteAllBytes(path, bytes);

            var inspection = ImportArtifactInspector.Inspect(path, sha);
            Assert.Equal(ArtifactIntegrity.Present, inspection.Status);
            Assert.True(inspection.CanOpen);
            Assert.Equal(sha, inspection.ActualSha256);
            Assert.Contains("matches the stored hash", inspection.Explanation);
            Assert.Contains("Cell,D,C", inspection.TextPreview);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Inspect_MissingFile_ReportsExpectedHash()
    {
        var sha = PanelImportReviewer.Hash("abc"u8.ToArray());
        var inspection = ImportArtifactInspector.Inspect(@"C:\missing\lot.csv", sha);
        Assert.Equal(ArtifactIntegrity.Missing, inspection.Status);
        Assert.False(inspection.CanOpen);
        Assert.Contains(sha, inspection.Explanation);
    }

    [Fact]
    public void Inspect_TamperedFile_IsHashMismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ab_art_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = "Cell,D\n1,+"u8.ToArray();
            var sha = PanelImportReviewer.Hash(original);
            var path = Path.Combine(dir, "lot.csv");
            File.WriteAllBytes(path, "Cell,D\n1,-"u8.ToArray());

            var inspection = ImportArtifactInspector.Inspect(path, sha);
            Assert.Equal(ArtifactIntegrity.HashMismatch, inspection.Status);
            Assert.True(inspection.CanOpen);
            Assert.Contains("does not match", inspection.Explanation);
            Assert.NotEqual(sha, inspection.ActualSha256);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LabCsvImport_CanBeInspectedAfterPersist()
    {
        using var iso = new IsolatedDatabase();
        var path = Path.Combine(Path.GetTempPath(), $"lab_art_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Cell,D,C,c\n1,+,-,+\n");
        try
        {
            var parsed = PanelCsvService.Import(path);
            var outcome = new LabPanelImportService(iso.Db).Import(parsed, new LabPanelImportRequest
            {
                Name = "Lab lot",
                LotNumber = "ART-1",
                Vendor = "Local",
                NumCells = 1,
                StartCell = 1
            });
            var panel = iso.Db.GetPanel(outcome.PanelId);
            Assert.True(panel!.HasImportArtifact);
            var inspection = ImportArtifactInspector.Inspect(panel);
            Assert.Equal(ArtifactIntegrity.Present, inspection.Status);
            Assert.Equal(panel.SourceSha256, inspection.ActualSha256);
            Assert.True(File.Exists(panel.SourceArtifactPath));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void VendorImport_CanBeInspectedAfterPersist()
    {
        using var iso = new IsolatedDatabase();
        var path = Path.Combine(Path.GetTempPath(), $"ven_art_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "Cell,D,C,c,E,e\n1,+,-,-,-,+\n");
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = "V-ART-1";
            parsed.SourceFormat = "csv";
            var id = new VendorPanelImportService(iso.Db).Persist(parsed);
            var panel = iso.Db.GetPanel(id);
            var inspection = ImportArtifactInspector.Inspect(panel!);
            Assert.Equal(ArtifactIntegrity.Present, inspection.Status);
            Assert.Contains("matches the stored hash", inspection.Explanation);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
