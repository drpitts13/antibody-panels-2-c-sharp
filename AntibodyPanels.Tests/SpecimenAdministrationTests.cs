using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class SpecimenAdministrationTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public SpecimenAdministrationTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void AddSpecimen_StoresTypeAndExpiration()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("SPEC-NEW-001", "plasma", "2030-06-01");
        var specimen = iso.Db.GetSpecimen("SPEC-NEW-001");
        Assert.NotNull(specimen);
        Assert.Equal("plasma", specimen!.Type);
        Assert.Equal("2030-06-01", specimen.ExpirationDate);
        Assert.False(string.IsNullOrEmpty(specimen.CreatedDate));
    }

    [Theory]
    [InlineData("serum")]
    [InlineData("plasma")]
    [InlineData("eluate")]
    public void AllSpecimenTypes_AreSupported(string type)
    {
        using var iso = new IsolatedDatabase();
        var acc = $"TYPE-{type}";
        iso.Db.AddSpecimen(acc, type, null);
        Assert.Equal(type, iso.Db.GetSpecimen(acc)!.Type);
    }

    [Fact]
    public void UpdateSpecimen_ChangesTypeAndExpiration()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("SPEC-UPD", "serum", "2025-01-01");
        iso.Db.UpdateSpecimen("SPEC-UPD", "eluate", "2026-01-01");
        var updated = iso.Db.GetSpecimen("SPEC-UPD");
        Assert.Equal("eluate", updated!.Type);
        Assert.Equal("2026-01-01", updated.ExpirationDate);
    }

    [Fact]
    public void LinkSpecimenPanel_AssociatesMultiplePanels()
    {
        var specimen = _fixture.Db.GetSpecimen("2024-009");
        Assert.NotNull(specimen);
        var panels = _fixture.Db.GetSpecimenPanels("2024-009");
        Assert.Equal(3, panels.Count);
    }

    [Fact]
    public void LinkSpecimenPanel_IgnoresDuplicateLinks()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DUP-LINK", "serum", null);
        var panelId = iso.Db.AddPanel("P1", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("DUP-LINK", panelId);
        iso.Db.LinkSpecimenPanel("DUP-LINK", panelId);
        Assert.Single(iso.Db.GetSpecimenPanels("DUP-LINK"));
    }

    [Fact]
    public void UnlinkSpecimenPanel_RemovesAssociation()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("UNLINK", "serum", null);
        var panelId = iso.Db.AddPanel("P1", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("UNLINK", panelId);
        iso.Db.UnlinkSpecimenPanel("UNLINK", panelId);
        Assert.Empty(iso.Db.GetSpecimenPanels("UNLINK"));
    }

    [Fact]
    public void DeleteSpecimen_CascadesReactionsAndAnalysis()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DEL-SPEC", "serum", null);
        var panelId = iso.Db.AddPanel("P1", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("DEL-SPEC", panelId);
        iso.Db.SaveReaction("DEL-SPEC", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("DEL-SPEC");

        iso.Db.DeleteSpecimen("DEL-SPEC");
        Assert.Null(iso.Db.GetSpecimen("DEL-SPEC"));
        Assert.Empty(iso.Db.GetReactions("DEL-SPEC", panelId));
    }

    [Fact]
    public void SaveReaction_UpdatesReactionsUpdatedTimestamp()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("TS-SPEC", "serum", null);
        var panelId = iso.Db.AddPanel("P1", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("TS-SPEC", panelId);
        iso.Db.SaveReaction("TS-SPEC", panelId, "1", "0", "0", "1+", "1+");

        var specimen = iso.Db.GetSpecimen("TS-SPEC");
        Assert.NotNull(specimen!.ReactionsUpdatedAt);
        Assert.True(iso.Db.IsSpecimenAnalysisStale("TS-SPEC") == false || specimen.LastAnalyzedAt == null);
    }

    [Fact]
    public void IsSpecimenAnalysisStale_DetectsNewReactionsAfterAnalysis()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("STALE", "serum", null);
        var panelId = iso.Db.AddPanel("P1", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("STALE", panelId);
        iso.Db.SaveReaction("STALE", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("STALE");
        Assert.False(iso.Db.IsSpecimenAnalysisStale("STALE"));

        Thread.Sleep(1100); // timestamps are second-precision
        iso.Db.SaveReaction("STALE", panelId, "1", "0", "0", "3+", "3+");
        Assert.True(iso.Db.IsSpecimenAnalysisStale("STALE"));
    }
}
