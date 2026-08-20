using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AntibodyPanels.Tests;

public class DatabaseArchiveTests
{
    [Fact]
    public void InspectArchive_ListsPurgedSpecimensAndFlagsLiveOnes()
    {
        using var iso = new IsolatedDatabase();
        Seed(iso, out var panelId, out _);
        var archivePath = NewArchivePath();
        try
        {
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            var inspection = iso.Db.InspectArchive(archivePath);

            Assert.Equal(1, inspection.SpecimenCount);
            Assert.Equal(1, inspection.RestorableCount);
            Assert.Equal(0, inspection.AlreadyInLiveCount);
            Assert.Contains(inspection.Specimens, s => s.AccessionNumber == "OLD-SPEC" && !s.ExistsInLive);
            Assert.Equal("2020-01-15", inspection.EarliestCreatedDate);
            Assert.True(inspection.PanelCount >= 1);
            Assert.True(inspection.FileBytes > 0);
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    [Fact]
    public void InspectArchive_MarksAccessionsAlreadyInLive()
    {
        using var iso = new IsolatedDatabase();
        Seed(iso, out _, out _);
        var archivePath = NewArchivePath();
        try
        {
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            iso.Db.RestoreArchive(archivePath);
            var inspection = iso.Db.InspectArchive(archivePath);

            Assert.Equal(1, inspection.AlreadyInLiveCount);
            Assert.Equal(0, inspection.RestorableCount);
            Assert.True(inspection.Specimens[0].ExistsInLive);
            Assert.Equal("Already in database", inspection.Specimens[0].Status);
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    [Fact]
    public void InspectArchive_RefusesLiveDatabasePath()
    {
        using var iso = new IsolatedDatabase();
        Assert.Throws<InvalidOperationException>(() => iso.Db.InspectArchive(iso.Db.DbPath));
    }

    [Fact]
    public void InspectArchive_RejectsFileWithoutSpecimensTable()
    {
        using var iso = new IsolatedDatabase();
        var path = Path.Combine(Path.GetTempPath(), $"not_archive_{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE foo (id INTEGER)";
                cmd.ExecuteNonQuery();
            }

            var ex = Assert.Throws<InvalidOperationException>(() => iso.Db.InspectArchive(path));
            Assert.Contains("archive", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void RestoreArchive_BringsBackPurgedSpecimenAndReactions()
    {
        using var iso = new IsolatedDatabase();
        Seed(iso, out var panelId, out _);
        iso.Db.SaveReaction("OLD-SPEC", panelId, "1", "0", "0", "2+", "2+");
        iso.Db.AddSpecimenAntibody("OLD-SPEC", "K", 0.9);

        var archivePath = NewArchivePath();
        try
        {
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            Assert.Null(iso.Db.GetSpecimen("OLD-SPEC"));

            var result = iso.Db.RestoreArchive(archivePath);
            Assert.Equal(1, result.SpecimensRestored);
            Assert.Equal(0, result.SpecimensSkipped);
            Assert.NotNull(iso.Db.GetSpecimen("OLD-SPEC"));
            Assert.Equal("2020-01-15", iso.Db.GetSpecimen("OLD-SPEC")!.CreatedDate);
            Assert.NotEmpty(iso.Db.GetAllSpecimenReactions("OLD-SPEC"));
            Assert.NotEmpty(iso.Db.GetReactions("OLD-SPEC", panelId));
            Assert.Contains(iso.Db.GetSpecimenAntibodies("OLD-SPEC"), a => a.Antibody == "K");
            Assert.Contains(iso.Db.GetSpecimenPanels("OLD-SPEC"), p => p.PanelId == panelId);
            Assert.NotNull(iso.Db.GetSpecimen("NEW-SPEC"));
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    [Fact]
    public void RestoreArchive_SkipsAccessionsAlreadyPresent()
    {
        using var iso = new IsolatedDatabase();
        Seed(iso, out _, out _);
        var archivePath = NewArchivePath();
        try
        {
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            iso.Db.RestoreArchive(archivePath);

            var second = iso.Db.RestoreArchive(archivePath);
            Assert.Equal(0, second.SpecimensRestored);
            Assert.Equal(1, second.SpecimensSkipped);
            Assert.Single(iso.Db.GetAllSpecimens().Where(s => s.AccessionNumber == "OLD-SPEC"));
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    [Fact]
    public void RestoreArchive_RestoresPanelDeletedFromLive()
    {
        using var iso = new IsolatedDatabase();
        Seed(iso, out var usedPanelId, out var unusedPanelId);
        var archivePath = NewArchivePath();
        try
        {
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            iso.Db.DeletePanel(usedPanelId);
            Assert.Null(iso.Db.GetPanel(usedPanelId));
            Assert.NotNull(iso.Db.GetPanel(unusedPanelId));

            var result = iso.Db.RestoreArchive(archivePath);
            Assert.Equal(1, result.SpecimensRestored);
            Assert.True(result.PanelsRestored >= 1);
            Assert.NotNull(iso.Db.GetPanel(usedPanelId));
            Assert.NotNull(iso.Db.GetSpecimen("OLD-SPEC"));
            Assert.Contains(iso.Db.GetSpecimenPanels("OLD-SPEC"), p => p.PanelId == usedPanelId);
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    [Fact]
    public void RestoreArchive_RefusesLiveDatabasePath()
    {
        using var iso = new IsolatedDatabase();
        Assert.Throws<InvalidOperationException>(() => iso.Db.RestoreArchive(iso.Db.DbPath));
    }

    private static void Seed(IsolatedDatabase iso, out int usedPanelId, out int unusedPanelId)
    {
        usedPanelId = iso.Db.AddPanel("Used Panel", "L1", "V", 1, null, false);
        unusedPanelId = iso.Db.AddPanel("Unused Panel", "L2", "V", 1, null, false);
        iso.Db.AddSpecimen("OLD-SPEC", "serum", null, createdDate: "2020-01-15");
        iso.Db.AddSpecimen("NEW-SPEC", "serum", null, createdDate: "2026-06-01");
        iso.Db.LinkSpecimenPanel("OLD-SPEC", usedPanelId);
        iso.Db.LinkSpecimenPanel("NEW-SPEC", usedPanelId);
    }

    private static string NewArchivePath() =>
        Path.Combine(Path.GetTempPath(), $"abpanels_archive_{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
