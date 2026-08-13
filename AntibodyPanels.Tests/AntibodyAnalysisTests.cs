using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class AntibodyAnalysisTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public AntibodyAnalysisTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void AnalyzeSpecimen_2024_001_IdentifiesAntiE()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: false);
        Assert.Equal("2024-001", result.SpecimenId);
        Assert.True(result.Suspected.ContainsKey("anti-E"),
            $"Expected anti-E in suspected. Found: {string.Join(", ", result.Suspected.Keys)}");
        Assert.True(result.Suspected["anti-E"] > 0.5);
        var antiEPattern = result.PatternMatches.FirstOrDefault(p => p.Antibody == "anti-E");
        Assert.NotNull(antiEPattern);
        Assert.True(antiEPattern!.Matches >= 3);
    }

    [Fact]
    public void AnalyzeSpecimen_2024_003_IdentifiesAntiK()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-003", updateDb: false);
        Assert.True(result.Suspected.ContainsKey("anti-K"),
            $"Expected anti-K. Found: {string.Join(", ", result.Suspected.Keys)}");
    }

    [Fact]
    public void AnalyzeSpecimen_2024_009_IdentifiesAntiC()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-009", updateDb: false);
        Assert.True(result.Suspected.ContainsKey("anti-c"),
            $"Expected anti-c. Found: {string.Join(", ", result.Suspected.Keys)}");
    }

    [Fact]
    public void AnalyzeSpecimen_NoAntibody_ReturnsEmptySuspected()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("TEST-NO-AB", updateDb: false);
        Assert.Empty(result.Suspected);
        Assert.NotEmpty(result.RuledOut);
    }

    [Fact]
    public void AnalyzeSpecimen_MultipleAntibodies_IdentifiesAtLeastTwo()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("TEST-MULTI-AB", updateDb: false);
        Assert.True(result.Suspected.Count >= 2,
            $"Expected multiple antibodies. Found: {string.Join(", ", result.Suspected.Keys)}");
        Assert.Contains(result.Suspected.Keys, k => k is "anti-E" or "anti-K");
    }

    [Fact]
    public void AnalyzeSpecimen_WithUpdateDb_PersistsResults()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PERSIST-AB", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 3, null, false);
        iso.Db.LinkSpecimenPanel("PERSIST-AB", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "E", "-");
        iso.Db.SaveReaction("PERSIST-AB", panelId, "1", "0", "0", "3+", "3+");
        iso.Db.SaveReaction("PERSIST-AB", panelId, "2", "0", "0", "0", "0");
        iso.Db.SaveReaction("PERSIST-AB", panelId, "3", "0", "0", "0", "0");

        iso.Analyzer.AnalyzeSpecimen("PERSIST-AB", updateDb: true);

        var antibodies = iso.Db.GetSpecimenAntibodies("PERSIST-AB");
        Assert.NotEmpty(antibodies);
        Assert.NotNull(iso.Db.GetSpecimen("PERSIST-AB")!.LastAnalyzedAt);
    }

    [Fact]
    public void AnalyzeSpecimen_NoReactions_ReturnsEmptyResult()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("NO-RXN", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("NO-RXN", panelId);

        var result = iso.Analyzer.AnalyzeSpecimen("NO-RXN");
        Assert.Empty(result.Suspected);
        Assert.Empty(result.RuledOut);
    }

    [Fact]
    public void AnalyzeSpecimen_IncludesStatisticsForEachSuspected()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: false);
        foreach (var ab in result.Suspected.Keys)
        {
            Assert.True(result.SuspectedStatistics.ContainsKey(ab));
            var stats = result.SuspectedStatistics[ab];
            Assert.InRange(stats.FisherPValue, 0, 1);
            Assert.InRange(stats.PatternScore, 0, 1);
            Assert.InRange(stats.CombinedScore, 0, 1);
        }
    }

    [Fact]
    public void AnalyzeSpecimen_DetectsAntibodyCombinationsWhenMultiplePresent()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("TEST-MULTI-AB", updateDb: false);
        if (result.Suspected.Count >= 2)
            Assert.NotEmpty(result.Combinations);
    }

    [Fact]
    public void AnalyzeSpecimen_GeneratesSuggestions()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("TEST-MULTI-AB", updateDb: false);
        Assert.NotNull(result.Suggestions);
        if (result.Suspected.Count > 1)
            Assert.Contains(result.Suggestions, s => s.Contains("multiple antibodies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PatternMatching_ReportsConfidenceForIdentifiedAntibody()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: false);
        var antiEPattern = result.PatternMatches.FirstOrDefault(p => p.Antibody == "anti-E");
        Assert.NotNull(antiEPattern);
        Assert.True(antiEPattern!.Matches > 0);
        Assert.True(antiEPattern.Confidence > 0);
    }

    [Fact]
    public void Rules_AffectRuleoutBehavior()
    {
        var rules = _fixture.Db.GetAllRules();
        Assert.Contains(rules, r => r.Name == "Anti-D C Exception" && r.HeterozygousOk);
        Assert.Contains(rules, r => r.MinRuleoutCount == 5);
    }
}
