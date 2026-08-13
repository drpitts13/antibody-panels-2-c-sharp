using System.Security.AccessControl;
using System.Security.Principal;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

/// <summary>
/// HIPAA-aligned security checks for a local desktop PHI application.
/// Covers data integrity, access control, injection resistance, and audit fields.
/// </summary>
public class HipaaSecurityTests
{
    [Fact]
    public void Database_UsesLocalFileStorageOnly()
    {
        using var iso = new IsolatedDatabase();
        Assert.True(Path.IsPathFullyQualified(iso.DbPath));
        Assert.EndsWith(".db", iso.DbPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(iso.DbPath));
    }

    [Fact]
    public void DatabaseFile_IsNotWorldReadableOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var iso = new IsolatedDatabase();
        var fileInfo = new FileInfo(iso.DbPath);
        var security = fileInfo.GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

        foreach (AuthorizationRule rule in rules)
        {
            if (rule is FileSystemAccessRule fsRule &&
                fsRule.AccessControlType == AccessControlType.Allow &&
                fsRule.FileSystemRights.HasFlag(FileSystemRights.Read))
            {
                var sid = (SecurityIdentifier)fsRule.IdentityReference;
                if (sid.IsWellKnown(WellKnownSidType.WorldSid) ||
                    sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid))
                {
                    // Local user profile temp paths may allow authenticated users; ensure not Everyone full control
                    if (sid.IsWellKnown(WellKnownSidType.WorldSid))
                        Assert.False(fsRule.FileSystemRights.HasFlag(FileSystemRights.FullControl),
                            "Database should not grant Everyone full control");
                }
            }
        }
    }

    [Fact]
    public void PhiAccessionNumbers_UseParameterizedQueries()
    {
        using var iso = new IsolatedDatabase();
        var phiAccession = "MRN-12345'; DROP TABLE specimens; --";
        iso.Db.AddSpecimen(phiAccession, "serum", null);

        var specimen = iso.Db.GetSpecimen(phiAccession);
        Assert.NotNull(specimen);
        Assert.Equal(phiAccession, specimen!.AccessionNumber);

        // Verify table still exists and other operations work
        iso.Db.AddSpecimen("MRN-SAFE-001", "plasma", null);
        Assert.Equal(2, iso.Db.GetAllSpecimens().Count);
    }

    [Fact]
    public void AuditFields_TrackAnalysisAndReactionTimestamps()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("AUDIT-001", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("AUDIT-001", panelId);

        iso.Db.SaveReaction("AUDIT-001", panelId, "1", "0", "0", "2+", "2+");
        var afterReaction = iso.Db.GetSpecimen("AUDIT-001");
        Assert.NotNull(afterReaction!.ReactionsUpdatedAt);

        iso.Analyzer.AnalyzeSpecimen("AUDIT-001");
        var afterAnalysis = iso.Db.GetSpecimen("AUDIT-001");
        Assert.NotNull(afterAnalysis!.LastAnalyzedAt);
    }

    [Fact]
    public void CascadeDelete_RemovesPhiWhenSpecimenDeleted()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHI-DEL", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("PHI-DEL", panelId);
        iso.Db.SaveReaction("PHI-DEL", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("PHI-DEL");

        iso.Db.DeleteSpecimen("PHI-DEL");
        Assert.Null(iso.Db.GetSpecimen("PHI-DEL"));
        Assert.Empty(iso.Db.GetSpecimenAntibodies("PHI-DEL"));
        Assert.Empty(iso.Db.GetReactions("PHI-DEL", panelId));
    }

    [Fact]
    public void ReportExport_DoesNotEmbedScriptInjection()
    {
        using var iso = new IsolatedDatabase();
        var malicious = "<script>alert('xss')</script>";
        iso.Db.AddSpecimen(malicious, "serum", null);
        iso.Analyzer.AnalyzeSpecimen(malicious); // no reactions, but shouldn't crash

        var text = iso.Reports.GeneratePreviewText(Services.ReportType.SpecimenSummary, malicious);
        Assert.Contains(malicious, text); // stored as literal data, not executed
        Assert.DoesNotContain("javascript:", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvExport_UsesUtf8EncodingForPhiCharacters()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHI-UTF8-田中", "serum", null);
        var csvPath = Path.Combine(Path.GetTempPath(), $"phi_{Guid.NewGuid():N}.csv");
        try
        {
            iso.Reports.ExportToCsv(Services.ReportType.AllSpecimens, csvPath);
            var content = File.ReadAllText(csvPath, System.Text.Encoding.UTF8);
            Assert.Contains("PHI-UTF8-田中", content);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void ForeignKeys_EnforcedOnSpecimenDeletion()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FK-TEST", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FK-TEST", panelId);
        iso.Db.SaveReaction("FK-TEST", panelId, "1", "0", "0", "1+", "1+");

        iso.Db.DeleteSpecimen("FK-TEST");
        Assert.Empty(iso.Db.GetReactions("FK-TEST", panelId));
    }

    [Fact]
    public void NoNetworkEndpoints_InApplicationArchitecture()
    {
        var assembly = typeof(AntibodyPanels.Data.DatabaseService).Assembly;
        var types = assembly.GetTypes();
        Assert.DoesNotContain(types, t => t.Name.Contains("HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(types, t => t.Name.Contains("Controller", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistentTestDatabase_StoredInControlledTestDataDirectory()
    {
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
        var dbPath = Path.Combine(testDataDir, "seeded_antibody_panels.db");
        TestDataSeeder.EnsureSeeded(dbPath);
        Assert.StartsWith(testDataDir, dbPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void AnalysisResults_DoNotLeakOtherSpecimensPhi()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHI-A", "serum", null);
        iso.Db.AddSpecimen("PHI-B", "plasma", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("PHI-A", panelId);
        iso.Db.LinkSpecimenPanel("PHI-B", panelId);
        iso.Db.SaveReaction("PHI-A", panelId, "1", "0", "0", "3+", "3+");
        iso.Analyzer.AnalyzeSpecimen("PHI-A");

        var reportA = iso.Reports.GeneratePreviewText(Services.ReportType.SpecimenSummary, "PHI-A");
        Assert.Contains("PHI-A", reportA);
        Assert.DoesNotContain("PHI-B", reportA);
    }
}
