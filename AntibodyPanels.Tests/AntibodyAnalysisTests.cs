using AntibodyPanels.Models;
using AntibodyPanels.Services;
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

    [Fact]
    public void IdentificationRule_TwoPlusTwoPanel_MeetsTwoPlusTwoNotThreePlusThree()
    {
        using var iso = new IsolatedDatabase();
        SeedTwoPlusTwoAntiE(iso, "ID-RULE-22");
        var previous = AppSettings.Current.IdentificationCellCount;
        try
        {
            AppSettings.Current.IdentificationCellCount = 2;
            var meets = iso.Analyzer.AnalyzeSpecimen("ID-RULE-22", updateDb: false);
            Assert.True(meets.Suspected.ContainsKey("anti-E"),
                $"Expected anti-E. Found: {string.Join(", ", meets.Suspected.Keys)}");
            var stats2 = meets.SuspectedStatistics["anti-E"];
            Assert.Equal(2, stats2.PositiveAgPositiveCount);
            Assert.Equal(2, stats2.NegativeAgNegativeCount);
            Assert.Equal(2, stats2.IdentificationRequired);
            Assert.True(stats2.MeetsIdentificationRule);
            Assert.Contains(meets.Suggestions,
                s => s.Contains("meets the 2 + 2 identification rule", StringComparison.OrdinalIgnoreCase));

            AppSettings.Current.IdentificationCellCount = 3;
            var incomplete = iso.Analyzer.AnalyzeSpecimen("ID-RULE-22", updateDb: false);
            Assert.True(incomplete.Suspected.ContainsKey("anti-E"));
            var stats3 = incomplete.SuspectedStatistics["anti-E"];
            Assert.Equal(2, stats3.PositiveAgPositiveCount);
            Assert.Equal(2, stats3.NegativeAgNegativeCount);
            Assert.Equal(3, stats3.IdentificationRequired);
            Assert.False(stats3.MeetsIdentificationRule);
            Assert.Contains(incomplete.Suggestions,
                s => s.Contains("2 of 3 required E+ reactive"));
            Assert.Contains(incomplete.Suggestions,
                s => s.Contains("2 of 3 required E- nonreactive"));
        }
        finally
        {
            AppSettings.Current.IdentificationCellCount = previous;
        }
    }

    [Fact]
    public void IdentificationRule_SameCellOnTwoRuns_CountsOnce()
    {
        using var iso = new IsolatedDatabase();
        var panelId = SeedTwoPlusTwoAntiE(iso, "ID-RULE-UNIQUE");
        var ficinId = iso.Db.AddPanelRun("ID-RULE-UNIQUE", panelId,
            CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        iso.Db.SaveReaction(ficinId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction(ficinId, "2", "0", "0", "3+", "NT");
        iso.Db.SaveReaction(ficinId, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(ficinId, "4", "0", "0", "0", "2+");

        var previous = AppSettings.Current.IdentificationCellCount;
        try
        {
            AppSettings.Current.IdentificationCellCount = 3;
            var result = iso.Analyzer.AnalyzeSpecimen("ID-RULE-UNIQUE", updateDb: false);
            Assert.True(result.Suspected.ContainsKey("anti-E"),
                $"Expected anti-E. Found: {string.Join(", ", result.Suspected.Keys)}");
            var stats = result.SuspectedStatistics["anti-E"];
            Assert.Equal(2, stats.PositiveAgPositiveCount);
            Assert.Equal(2, stats.NegativeAgNegativeCount);
        }
        finally
        {
            AppSettings.Current.IdentificationCellCount = previous;
        }
    }

    [Fact]
    public void ClinicalIdentificationReport_IncludesIdentificationRuleStatus()
    {
        using var iso = new IsolatedDatabase();
        SeedTwoPlusTwoAntiE(iso, "ID-RULE-RPT");
        var previous = AppSettings.Current.IdentificationCellCount;
        try
        {
            AppSettings.Current.IdentificationCellCount = 3;
            var text = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ID-RULE-RPT");
            Assert.Contains("anti-E", text);
            Assert.Contains("Incomplete", text);
            Assert.Contains("Ag+ reactive", text);
        }
        finally
        {
            AppSettings.Current.IdentificationCellCount = previous;
        }
    }

    private static int SeedTwoPlusTwoAntiE(IsolatedDatabase iso, string specimenId)
    {
        iso.Db.AddSpecimen(specimenId, "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel(specimenId, panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "E", "-");
        iso.Db.SaveReaction(specimenId, panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction(specimenId, panelId, "2", "0", "0", "3+", "NT");
        iso.Db.SaveReaction(specimenId, panelId, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(specimenId, panelId, "4", "0", "0", "0", "2+");
        return panelId;
    }
}
