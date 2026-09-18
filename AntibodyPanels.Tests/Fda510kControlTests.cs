using System.Text.Json;
using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

/// <summary>
/// Software controls that support FDA 510(k) device-software documentation.
/// </summary>
public class Fda510kControlTests
{
    [Fact]
    public void FreshDatabase_HasNoSeededSpecimens()
    {
        using var iso = new IsolatedDatabase();
        Assert.Empty(iso.Db.GetAllSpecimens());
        Assert.Empty(iso.Db.GetAllPanels());
    }

    [Fact]
    public void SoftwareIdentity_ExposesAssemblyVersionAndIntendedUse()
    {
        Assert.False(string.IsNullOrWhiteSpace(SoftwareIdentity.Version));
        Assert.DoesNotContain("Version 2.0 (C# / WPF)", SoftwareIdentity.AboutText());
        Assert.Contains(SoftwareIdentity.Version, SoftwareIdentity.AboutText());
        Assert.Contains("does not issue or release blood units", SoftwareIdentity.IntendedUse);
        Assert.Contains("INTENDED USE:", SoftwareIdentity.ReportPreamble());
        Assert.Contains(SoftwareIdentity.Version, SoftwareIdentity.ReportPreamble());
    }

    [Fact]
    public void ConfirmAndClear_WriteAuditEvents_AndRequireReason()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FDA-001", "serum", null);

        iso.Db.SetSpecimenFinalCall("FDA-001", "anti-E", "comment", "DP");
        Assert.Contains(iso.Db.GetAuditEvents(), e => e.Action == "confirm_final_call" && e.EntityId == "FDA-001");
        Assert.False(string.IsNullOrWhiteSpace(iso.Db.GetAuditEvents().First().Operator));

        Assert.Throws<ArgumentException>(() => iso.Db.ClearSpecimenFinalCall("FDA-001", " "));
        Assert.True(iso.Db.GetSpecimen("FDA-001")!.HasFinalCall);

        iso.Db.ClearSpecimenFinalCall("FDA-001", "incorrect antigen assignment");
        Assert.False(iso.Db.GetSpecimen("FDA-001")!.HasFinalCall);
        Assert.Contains(iso.Db.GetAuditEvents(),
            e => e.Action == "clear_final_call" && e.Reason == "incorrect antigen assignment");
    }

    [Fact]
    public void ConfirmedSpecimen_LocksReactionsAndAnalysisWrites()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FDA-LOCK", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FDA-LOCK", panelId);
        iso.Db.SaveReaction("FDA-LOCK", panelId, "1", "0", "0", "2+", "2+");
        iso.Db.SetSpecimenFinalCall("FDA-LOCK", "anti-E", null, "DP");

        Assert.True(iso.Db.IsSpecimenLocked("FDA-LOCK"));
        Assert.Throws<RecordLockedException>(() =>
            iso.Db.SaveReaction("FDA-LOCK", panelId, "1", "0", "0", "3+", "3+"));
        Assert.Throws<RecordLockedException>(() =>
            iso.Analyzer.AnalyzeSpecimen("FDA-LOCK", updateDb: true));

        var preview = iso.Analyzer.AnalyzeSpecimen("FDA-LOCK", updateDb: false);
        Assert.Equal("FDA-LOCK", preview.SpecimenId);
    }

    [Fact]
    public void AnalyzeSpecimen_PersistsTraceabilitySnapshot()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FDA-SNAP", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FDA-SNAP", panelId);
        iso.Db.SaveReaction("FDA-SNAP", panelId, "1", "0", "0", "2+", "2+");

        iso.Analyzer.AnalyzeSpecimen("FDA-SNAP");
        var snap = iso.Db.GetLatestAnalysisSnapshot("FDA-SNAP");
        Assert.NotNull(snap);
        Assert.Equal(SoftwareIdentity.Version, snap!.SoftwareVersion);
        Assert.False(string.IsNullOrWhiteSpace(snap.InputFingerprint));
        Assert.Contains("ProbabilityThreshold", snap.SettingsJson);
        Assert.False(string.IsNullOrWhiteSpace(snap.RuledOutJson));
        Assert.False(string.IsNullOrWhiteSpace(snap.SuspectedJson));
        Assert.False(string.IsNullOrWhiteSpace(snap.AcsJson));
        Assert.NotNull(JsonDocument.Parse(snap.SettingsJson));
    }

    [Fact]
    public void Reports_IncludeIntendedUseVersionAndTraceLine()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FDA-RPT", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FDA-RPT", panelId);
        iso.Db.SaveReaction("FDA-RPT", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("FDA-RPT");

        var analysis = iso.Reports.GeneratePreviewText(ReportType.AnalysisResults, "FDA-RPT");
        Assert.Contains("INTENDED USE:", analysis);
        Assert.Contains(SoftwareIdentity.Version, analysis);
        Assert.Contains("Input fingerprint:", analysis);

        var clinical = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "FDA-RPT");
        Assert.Contains("INTENDED USE:", clinical);
        Assert.Contains("Software", clinical);
    }

    [Fact]
    public void AppLog_WritesVersionedLineWithoutThrowing()
    {
        AppLog.Info("fda-510k-control-test");
        var today = Path.Combine(AppLog.LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
        Assert.True(File.Exists(today));
        var text = File.ReadAllText(today);
        Assert.Contains("fda-510k-control-test", text);
        Assert.Contains($"v{SoftwareIdentity.Version}", text);
    }

    [Fact]
    public void SettingsChange_CanBeAudited()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AppendAudit("update_settings", "settings", "lab", null,
            "{\"ProbabilityThreshold\":0.5}", "{\"ProbabilityThreshold\":0.8}");
        Assert.Contains(iso.Db.GetAuditEvents(), e => e.Action == "update_settings");
    }
}
