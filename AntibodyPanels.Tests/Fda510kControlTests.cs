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
        Assert.Equal(AnalysisRuleTrace.EngineVersion, snap.RuleEngineVersion);
        Assert.False(string.IsNullOrWhiteSpace(snap.RulesJson));
        Assert.Contains("DefaultMinRuleoutCount", snap.RulesJson);
        Assert.Contains("No per-antibody rule overrides", iso.Analyzer.AnalyzeSpecimen("FDA-SNAP", updateDb: false).RuleConfigurationNote);
    }

    [Fact]
    public void Snapshot_RecordsPerAntibodyRuleOverride_AfterChange()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FDA-RULES", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FDA-RULES", panelId);
        iso.Db.SaveReaction("FDA-RULES", panelId, "1", "0", "0", "0", "2+");
        iso.Db.AddRule("Anti-D C Exception", "het C", "anti-D", "C", true, 3);

        iso.Analyzer.AnalyzeSpecimen("FDA-RULES");
        var snap = iso.Db.GetLatestAnalysisSnapshot("FDA-RULES");
        Assert.Equal(AnalysisRuleTrace.EngineVersion, snap!.RuleEngineVersion);
        Assert.Contains("anti-D", snap.RulesJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"MinRuleoutCount\":3", snap.RulesJson);
        Assert.Contains("heterozygous C allowed", iso.Analyzer.AnalyzeSpecimen("FDA-RULES", updateDb: false).RuleConfigurationNote);

        var rule = iso.Db.GetAllRules().Single(r => r.Antibody == "anti-D");
        iso.Db.UpdateRule(rule.RuleId, rule.Name, rule.Description, "anti-D", "C", true, 2);
        iso.Analyzer.AnalyzeSpecimen("FDA-RULES");
        var later = iso.Db.GetLatestAnalysisSnapshot("FDA-RULES");
        Assert.Contains("\"MinRuleoutCount\":2", later!.RulesJson);
        Assert.DoesNotContain("\"MinRuleoutCount\":3", later.RulesJson);
        var report = iso.Reports.GeneratePreviewText(ReportType.AnalysisResults, "FDA-RULES");
        Assert.Contains($"Rule engine v{AnalysisRuleTrace.EngineVersion}", report);
        Assert.Contains("Operator", report);
    }

    [Fact]
    public void Snapshot_RecordsLabInitialsAndWindowsLogin()
    {
        using var iso = new IsolatedDatabase();
        var previous = AppSettings.Current.DefaultIdentifiedBy;
        try
        {
            AppSettings.Current.DefaultIdentifiedBy = "ZX";
            iso.Db.AddSpecimen("FDA-OP", "serum", null);
            var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
            iso.Db.LinkSpecimenPanel("FDA-OP", panelId);
            iso.Db.SaveReaction("FDA-OP", panelId, "1", "0", "0", "2+", "2+");
            iso.Analyzer.AnalyzeSpecimen("FDA-OP");
            var snap = iso.Db.GetLatestAnalysisSnapshot("FDA-OP");
            Assert.Equal("ZX", snap!.AnalyzedBy);
            var live = iso.Analyzer.AnalyzeSpecimen("FDA-OP", updateDb: false);
            Assert.Equal("ZX", live.AnalyzedBy);
            Assert.Contains("operator ZX", live.OperatorNote, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(Environment.UserName, "ZX", StringComparison.OrdinalIgnoreCase))
                Assert.Contains("Windows login", live.OperatorNote);
            Assert.Contains("not a diagnosis", live.OperatorNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("operator ZX", AnalysisExplainer.FormatDocument(live), StringComparison.OrdinalIgnoreCase);
            var report = iso.Reports.GeneratePreviewText(ReportType.AnalysisResults, "FDA-OP");
            Assert.Contains("Operator ZX", report);
        }
        finally
        {
            AppSettings.Current.DefaultIdentifiedBy = previous;
        }
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
        Assert.Contains($"Rule engine v{AnalysisRuleTrace.EngineVersion}", analysis);

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
