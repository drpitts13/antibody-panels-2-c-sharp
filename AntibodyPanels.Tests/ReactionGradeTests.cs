using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ReactionGradeTests
{
    [Theory]
    [InlineData("w+", "w+")]
    [InlineData("W+", "w+")]
    [InlineData("weak", "w+")]
    [InlineData("MF", "MF")]
    [InlineData("mixed-field", "MF")]
    [InlineData("H", "H")]
    [InlineData("hemolysis", "H")]
    [InlineData("2+", "2+")]
    [InlineData("0", "0")]
    public void Normalize_MapsAliases(string raw, string expected)
    {
        Assert.Equal(expected, ReactionGrade.Normalize(raw));
    }

    [Fact]
    public void WeakAndSpecial_ArePositive_NotNegative()
    {
        Assert.True(ReactionGrade.IsPositive("w+"));
        Assert.True(ReactionGrade.IsPositive("MF"));
        Assert.True(ReactionGrade.IsPositive("H"));
        Assert.False(ReactionGrade.IsNegative("w+"));
        Assert.False(ReactionGrade.IsAbsent("w+"));
        Assert.Equal(0.5, ReactionGrade.Strength("w+"));
        Assert.Equal(1, ReactionGrade.Strength("MF"));
        Assert.Equal(4, ReactionGrade.Strength("H"));
    }

    [Fact]
    public void Reaction_WPlusAhg_IsPositive()
    {
        var rxn = new Reaction { IS = "NT", C37 = "NT", AHG = "w+", CC = "NT" };
        Assert.True(rxn.IsPositive);
        Assert.False(rxn.IsNegative);
    }

    [Fact]
    public void RunContext_WPlusIsStrongerThanZero()
    {
        var ctx = new RunContext(new PanelRun());
        var rxn = new Reaction { IS = "0", C37 = "0", AHG = "w+", CC = "NT" };
        Assert.True(ctx.IsPositive(rxn));
        Assert.False(ctx.IsNegative(rxn));
        var (phase, value) = ctx.GetStrongestPhase(rxn);
        Assert.Equal("AHG", phase);
        Assert.Equal("w+", value);
    }

    [Fact]
    public void Analyzer_WPlusEPositive_DoesNotRuleOutAntiE()
    {
        using var iso = new IsolatedDatabase();
        SeedECell(iso, "RG-W", ahg: "w+");
        var result = iso.Analyzer.AnalyzeSpecimen("RG-W", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.Contains(result.SpecialReactionNotes, n => n.Contains("w+") && n.Contains("cell 1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Suggestions, s => s.Contains("w+"));
    }

    [Fact]
    public void Analyzer_MixedFieldEPositive_DoesNotRuleOut_AndExplains()
    {
        using var iso = new IsolatedDatabase();
        SeedECell(iso, "RG-MF", ahg: "MF");
        var result = iso.Analyzer.AnalyzeSpecimen("RG-MF", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.Contains(result.SpecialReactionNotes, n => n.Contains("mixed-field"));
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("mixed-field", doc);
    }

    [Fact]
    public void Analyzer_HemolysisEPositive_DoesNotRuleOut()
    {
        using var iso = new IsolatedDatabase();
        SeedECell(iso, "RG-H", ahg: "H");
        var result = iso.Analyzer.AnalyzeSpecimen("RG-H", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.Contains(result.SpecialReactionNotes, n => n.Contains("hemolysis"));
    }

    [Fact]
    public void Analyzer_ZeroAhg_StillRulesOutAntiE()
    {
        using var iso = new IsolatedDatabase();
        SeedECell(iso, "RG-0", ahg: "0", extraNegative: true);
        var result = iso.Analyzer.AnalyzeSpecimen("RG-0", updateDb: false);
        Assert.True(result.RuledOut.ContainsKey("anti-E"));
        Assert.Empty(result.SpecialReactionNotes);
    }

    private static void SeedECell(IsolatedDatabase iso, string specimenId, string ahg,
        bool extraNegative = false)
    {
        iso.Db.AddSpecimen(specimenId, "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", extraNegative ? 2 : 1, null, false);
        iso.Db.LinkSpecimenPanel(specimenId, panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "e", "-");
        iso.Db.SaveReaction(specimenId, panelId, "1", "0", "0", ahg, ahg == "0" ? "2+" : "NT");
        if (extraNegative)
        {
            iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "-");
            iso.Db.SaveReaction(specimenId, panelId, "2", "0", "0", "0", "2+");
        }
    }
}
