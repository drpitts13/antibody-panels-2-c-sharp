using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class DatabasePurgeTests
{
    [Fact]
    public void CapacityStatus_FlagsNearCapacityAgainstInjectedMax()
    {
        using var iso = new IsolatedDatabase();
        var size = iso.Db.GetFileSizeBytes();
        Assert.True(size > 0);

        var near = iso.Db.GetCapacityStatus(Math.Max(1, size / 2));
        Assert.True(near.IsNearCapacity);
        Assert.True(near.PercentUsed >= DatabaseCapacityStatus.WarningPercent);

        var plenty = iso.Db.GetCapacityStatus(size * 10);
        Assert.False(plenty.IsNearCapacity);
        Assert.True(plenty.PercentUsed < DatabaseCapacityStatus.WarningPercent);
        Assert.Equal(size, plenty.FileBytes);
    }

    [Fact]
    public void ApplyMaxPageCount_DoesNotThrow()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.ApplyMaxPageCount(DatabaseCapacityStatus.BytesFromMb(50));
        iso.Db.ApplyMaxPageCount(1);
    }

    [Fact]
    public void LabSettings_Clamp_KeepsMaxDatabaseSizeInRange()
    {
        var low = new LabSettings { MaxDatabaseSizeMb = 1 };
        low.Clamp();
        Assert.Equal(50, low.MaxDatabaseSizeMb);

        var high = new LabSettings { MaxDatabaseSizeMb = 50_000 };
        high.Clamp();
        Assert.Equal(10240, high.MaxDatabaseSizeMb);

        var ok = new LabSettings { MaxDatabaseSizeMb = 500 };
        ok.Clamp();
        Assert.Equal(500, ok.MaxDatabaseSizeMb);
    }

    [Fact]
    public void CountSpecimensCreatedBefore_MatchesRowsThatWouldBePurged()
    {
        using var iso = new IsolatedDatabase();
        SeedOldAndNew(iso, out var usedPanelId, out _);

        var cutoff = "2024-01-01";
        var preview = iso.Db.CountSpecimensCreatedBefore(cutoff);
        var result = iso.Db.PurgeSpecimensCreatedBefore(cutoff);

        Assert.Equal(1, preview);
        Assert.Equal(preview, result.SpecimensDeleted);
        Assert.Null(iso.Db.GetSpecimen("OLD-SPEC"));
        Assert.NotNull(iso.Db.GetSpecimen("NEW-SPEC"));
        Assert.NotNull(iso.Db.GetPanel(usedPanelId));
    }

    [Fact]
    public void KeepDaysCutoff_DeletesOnlyOlderSpecimens()
    {
        using var iso = new IsolatedDatabase();
        var cutoff = DatabaseService.CutoffForKeepDays(30);
        iso.Db.AddSpecimen("OLD-KEEPDAYS", "serum", null, createdDate: "2020-01-01");
        iso.Db.AddSpecimen("ON-CUTOFF", "serum", null, createdDate: cutoff);
        iso.Db.AddSpecimen("NEW-KEEPDAYS", "serum", null, createdDate: DateTime.Today.ToString("yyyy-MM-dd"));

        var result = iso.Db.PurgeSpecimensCreatedBefore(cutoff);

        Assert.Equal(1, result.SpecimensDeleted);
        Assert.Null(iso.Db.GetSpecimen("OLD-KEEPDAYS"));
        Assert.NotNull(iso.Db.GetSpecimen("ON-CUTOFF"));
        Assert.NotNull(iso.Db.GetSpecimen("NEW-KEEPDAYS"));
    }

    [Fact]
    public void Purge_CascadesReactionsAndAnalysis_LeavesNewerSpecimen()
    {
        using var iso = new IsolatedDatabase();
        SeedOldAndNew(iso, out var panelId, out _);
        iso.Db.SaveReaction("OLD-SPEC", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("OLD-SPEC");
        iso.Db.SaveReaction("NEW-SPEC", panelId, "1", "0", "0", "1+", "1+");

        iso.Db.PurgeSpecimensCreatedBefore("2024-01-01");

        Assert.Null(iso.Db.GetSpecimen("OLD-SPEC"));
        Assert.Empty(iso.Db.GetReactions("OLD-SPEC", panelId));
        Assert.Empty(iso.Db.GetSpecimenAntibodies("OLD-SPEC"));
        Assert.NotNull(iso.Db.GetSpecimen("NEW-SPEC"));
        Assert.NotEmpty(iso.Db.GetReactions("NEW-SPEC", panelId));
    }

    [Fact]
    public void Purge_LeavesPanelsAndRulesInLiveDatabase()
    {
        using var iso = new IsolatedDatabase();
        SeedOldAndNew(iso, out var usedPanelId, out var unusedPanelId);
        var ruleId = iso.Db.AddRule("Keep me", "desc", "K", null, false, 3);

        iso.Db.PurgeSpecimensCreatedBefore("2024-01-01");

        Assert.NotNull(iso.Db.GetPanel(usedPanelId));
        Assert.NotNull(iso.Db.GetPanel(unusedPanelId));
        Assert.Contains(iso.Db.GetAllRules(), r => r.RuleId == ruleId);
    }

    [Fact]
    public void Purge_WithArchive_CopiesPurgedSpecimensAndReferencedPanels()
    {
        using var iso = new IsolatedDatabase();
        SeedOldAndNew(iso, out var usedPanelId, out var unusedPanelId);
        iso.Db.SaveReaction("OLD-SPEC", usedPanelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("OLD-SPEC");

        var extra = AntigenConstants.WarehouseAntigens[0];
        iso.Db.AddPanelExtraAntigen(usedPanelId, extra);

        var archivePath = Path.Combine(Path.GetTempPath(), $"abpanels_archive_{Guid.NewGuid():N}.db");
        try
        {
            var result = iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", archivePath);
            Assert.Equal(1, result.SpecimensDeleted);
            Assert.Equal(archivePath, result.ArchivePath);
            Assert.True(File.Exists(archivePath));
            Assert.Null(iso.Db.GetSpecimen("OLD-SPEC"));
            Assert.NotNull(iso.Db.GetSpecimen("NEW-SPEC"));
            Assert.NotNull(iso.Db.GetPanel(usedPanelId));
            Assert.NotNull(iso.Db.GetPanel(unusedPanelId));

            using var archive = new DatabaseService(archivePath);
            Assert.NotNull(archive.GetSpecimen("OLD-SPEC"));
            Assert.Null(archive.GetSpecimen("NEW-SPEC"));
            Assert.NotNull(archive.GetPanel(usedPanelId));
            Assert.Null(archive.GetPanel(unusedPanelId));
            Assert.NotEmpty(archive.GetReactions("OLD-SPEC", usedPanelId));
            Assert.True(archive.PanelHasExtraAntigen(usedPanelId, extra));
            Assert.Empty(archive.GetAllRules());
        }
        finally
        {
            try { File.Delete(archivePath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Purge_Vacuum_CompletesAndReportsFileSize()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("OLD-VAC", "serum", null, createdDate: "2019-06-01");
        iso.Db.AddSpecimen("NEW-VAC", "serum", null, createdDate: DateTime.Today.ToString("yyyy-MM-dd"));

        var result = iso.Db.PurgeSpecimensCreatedBefore("2024-01-01");
        Assert.Equal(1, result.SpecimensDeleted);
        Assert.True(result.FileSizeBytesAfter > 0);
        Assert.True(File.Exists(iso.Db.DbPath));
    }

    [Fact]
    public void Purge_RefusesArchivePathEqualToLiveDatabase()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("OLD-PATH", "serum", null, createdDate: "2020-01-01");
        Assert.Throws<InvalidOperationException>(() =>
            iso.Db.PurgeSpecimensCreatedBefore("2024-01-01", iso.Db.DbPath));
        Assert.NotNull(iso.Db.GetSpecimen("OLD-PATH"));
    }

    [Fact]
    public void FormatBytes_UsesReadableUnits()
    {
        Assert.Equal("512 B", DatabaseCapacityStatus.FormatBytes(512));
        Assert.Contains("KB", DatabaseCapacityStatus.FormatBytes(2048));
        Assert.Contains("MB", DatabaseCapacityStatus.FormatBytes(2 * 1024 * 1024));
        Assert.Equal(500L * 1024 * 1024, DatabaseCapacityStatus.BytesFromMb(500));
    }

    private static void SeedOldAndNew(IsolatedDatabase iso, out int usedPanelId, out int unusedPanelId)
    {
        usedPanelId = iso.Db.AddPanel("Used Panel", "L1", "V", 1, null, false);
        unusedPanelId = iso.Db.AddPanel("Unused Panel", "L2", "V", 1, null, false);
        iso.Db.AddSpecimen("OLD-SPEC", "serum", null, createdDate: "2020-01-15");
        iso.Db.AddSpecimen("NEW-SPEC", "serum", null, createdDate: "2026-06-01");
        iso.Db.LinkSpecimenPanel("OLD-SPEC", usedPanelId);
        iso.Db.LinkSpecimenPanel("NEW-SPEC", usedPanelId);
    }
}
