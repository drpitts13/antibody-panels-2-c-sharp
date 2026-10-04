using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class HtlaTitrationTests
{
    [Fact]
    public void TiterParser_ReadsNotesAndIgnoresReactionGrades()
    {
        Assert.Equal(64, TiterParser.Highest("AHG titer 64; neutralized with plasma"));
        Assert.Equal(256, TiterParser.Highest("titer 1:256"));
        Assert.Equal(128, TiterParser.Highest(null, new Dictionary<string, string>
        {
            ["Titer"] = "128",
            ["AHG"] = "1+"
        }));
        Assert.Null(TiterParser.Highest(null, new Dictionary<string, string> { ["Titer"] = "1+" }));
        Assert.Null(TiterParser.Highest("no titer recorded"));
        Assert.True(TiterParser.SuggestsHtlaTiter(64));
        Assert.False(TiterParser.SuggestsHtlaTiter(4));
    }

    [Fact]
    public void TiterParser_ReadsSerialDilutionGrid_LastReactiveIsEndpoint()
    {
        Assert.Equal(4, ExtraPhaseParser.TryParseDilution("Dil4", out var d4) ? d4 : 0);
        Assert.Equal(16, ExtraPhaseParser.TryParseDilution("1:16", out var d16) ? d16 : 0);
        Assert.True(ExtraPhaseParser.TryParseDilution("Neat", out var neat) && neat == 1);
        Assert.False(ExtraPhaseParser.IsDilution("Titer"));
        Assert.False(ExtraPhaseParser.IsDilution("Gel"));
        Assert.False(ExtraPhaseParser.IsIatLike("Dil64"));

        var grid = new Dictionary<string, string>
        {
            ["Dil1"] = "3+",
            ["Dil2"] = "2+",
            ["Dil4"] = "1+",
            ["Dil8"] = "0",
            ["AHG"] = "w+"
        };
        Assert.Equal(4, TiterParser.Highest(null, grid));
        Assert.Equal(4, TiterParser.Highest(null, new Dictionary<string, string>
        {
            ["Dil4"] = "1+",
            ["Titer"] = "1+"
        }));
        Assert.Null(TiterParser.Highest(null, new Dictionary<string, string> { ["Titer"] = "1+" }));

        var prozone = new Dictionary<string, string>
        {
            ["Dil1"] = "0",
            ["Dil4"] = "2+",
            ["Dil16"] = "w+",
            ["Dil32"] = "0"
        };
        Assert.Equal(16, TiterParser.Highest(null, prozone));
        Assert.Equal(64, TiterParser.Highest("titer 32", new Dictionary<string, string>
        {
            ["1:32"] = "1+",
            ["1:64"] = "w+",
            ["1:128"] = "0"
        }));
    }

    [Fact]
    public void DilutionGrades_DoNotCountAsPanelPhases()
    {
        var ctx = new RunContext(new PanelRun());
        var rxn = new Reaction
        {
            CellNumber = "1",
            IS = "0",
            C37 = "0",
            AHG = "0",
            CC = "2+",
            ExtraPhases = { ["Dil64"] = "1+", ["Dil128"] = "0" }
        };
        Assert.True(ctx.IsNegative(rxn));
        Assert.False(ctx.IsPositive(rxn));
        Assert.Equal(64, TiterParser.Highest(null, rxn.ExtraPhases));
    }

    [Fact]
    public void HighTiter_StrengthensHtlaNote_LowTiter_Warns()
    {
        var high = HtlaResult();
        HtlaTitrationNotes.Apply(high, 64);
        var highNote = high.ReactionPatterns.Single(n => n.Kind == ReactionPatternClassifier.Htla);
        Assert.Contains("titer of 64", highNote.Explanation);
        Assert.Contains("high-titer", highNote.Explanation);
        Assert.Contains("Not a diagnosis", highNote.Explanation);

        var low = HtlaResult();
        HtlaTitrationNotes.Apply(low, 4);
        var lowNote = low.ReactionPatterns.Single(n => n.Kind == ReactionPatternClassifier.Htla);
        Assert.Contains("lower than typical HTLA", lowNote.Explanation);
        Assert.DoesNotContain("supports an HTLA-like high-titer", lowNote.Explanation);
    }

    [Fact]
    public void RareAntigenReader_UsesTypedAndSpecialTypes_NotUntyped()
    {
        var cell = new PanelCell { CellNumber = "3", SpecialTypes = "Yt(a-), Vel-" };
        cell.SetAntigen("Kpb", "-");
        cell.SetAntigen("Yta", "NT");
        var negs = RareAntigenReader.Negatives(cell);
        Assert.DoesNotContain("Kpb", negs);
        Assert.Contains("Yta", negs);
        Assert.Contains("Vel", negs);
        Assert.DoesNotContain("k", negs);
    }

    [Fact]
    public void HtlaPattern_RecommendsUnusedYtaNegativeCell()
    {
        var result = HtlaResult();
        var stock = new Panel { PanelId = 2, Name = "Rare cells", LotNumber = "R" };
        var yta = new PanelCell { CellNumber = "5", SpecialTypes = "Yt(a-)" };
        var ordinary = new PanelCell { CellNumber = "6" };
        ordinary.SetAntigen("E", "+");
        ordinary.SetAntigen("e", "-");

        Assert.True(SelectedCellRecommender.NeedsRareNegativeCell(result));
        var recs = SelectedCellRecommender.Recommend(
            result,
            Array.Empty<(int, string)>(),
            new[] { (stock, ordinary), (stock, yta) });

        Assert.Contains(recs, r => r.CellNumber == "5");
        var rare = recs.First(r => r.CellNumber == "5");
        Assert.Contains("Yta", rare.Explanation);
        Assert.Contains("HTLA or high-prevalence", rare.Explanation);
        Assert.DoesNotContain(recs, r => r.CellNumber == "6");
    }

    [Fact]
    public void NoHtlaPattern_DoesNotRecommendYtaNegativeAlone()
    {
        var result = new AnalysisResult();
        var stock = new Panel { PanelId = 2, Name = "Rare cells" };
        var yta = new PanelCell { CellNumber = "5", SpecialTypes = "Yt(a-)" };
        var recs = SelectedCellRecommender.Recommend(
            result, Array.Empty<(int, string)>(), new[] { (stock, yta) });
        Assert.Empty(recs);
    }

    [Fact]
    public void Analyzer_HtlaWithTiter_DoesNotAutoIdentify()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("HTLA-64", "serum", null, notes: "titer 64");
        var usedId = iso.Db.AddPanel("Used", "U", "V", 8, null, false);
        var rareId = iso.Db.AddPanel("Selectogen rare", "S", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("HTLA-64", usedId);
        var used = iso.Db.GetPanelCells(usedId);
        var runId = iso.Db.GetOrCreateDefaultRun("HTLA-64", usedId);
        foreach (var cell in used)
            iso.Db.SaveReaction(runId, cell.CellNumber, "0", "0", "w+", "NT");
        var rare = iso.Db.GetPanelCells(rareId);
        iso.Db.UpdatePanelCellMetadata(rare[0].Id, null, null, "Yt(a-)");

        var result = iso.Analyzer.AnalyzeSpecimen("HTLA-64", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Htla);
        Assert.Contains(result.ReactionPatterns,
            n => n.Kind == ReactionPatternClassifier.Htla && n.Explanation.Contains("titer of 64"));
        Assert.DoesNotContain(result.Suggestions, s => s.Contains("identified", StringComparison.OrdinalIgnoreCase)
            && s.Contains("HTLA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SelectedCellRecommendations,
            r => r.Explanation.Contains("Yta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyzer_TiterGridEndpoint_StrengthensHtla_WithoutIdentifying()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("HTLA-GRID", "serum", null);
        var usedId = iso.Db.AddPanel("Used", "U", "V", 8, null, false);
        iso.Db.LinkSpecimenPanel("HTLA-GRID", usedId);
        var used = iso.Db.GetPanelCells(usedId);
        var runId = iso.Db.GetOrCreateDefaultRun("HTLA-GRID", usedId);
        foreach (var cell in used)
        {
            var extras = cell.CellNumber == "1"
                ? new Dictionary<string, string>
                {
                    ["Dil1"] = "3+",
                    ["Dil4"] = "2+",
                    ["Dil16"] = "1+",
                    ["Dil64"] = "w+",
                    ["Dil128"] = "0"
                }
                : null;
            iso.Db.SaveReaction(runId, cell.CellNumber, "0", "0", "w+", "NT", extras);
        }

        var result = iso.Analyzer.AnalyzeSpecimen("HTLA-GRID", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Htla);
        Assert.Contains(result.ReactionPatterns,
            n => n.Kind == ReactionPatternClassifier.Htla && n.Explanation.Contains("titer of 64"));
        Assert.DoesNotContain(result.Suggestions, s => s.Contains("identified", StringComparison.OrdinalIgnoreCase)
            && s.Contains("HTLA", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.RuledOut.ContainsKey("anti-Jk3"));
    }

    [Fact]
    public void PreferencesTiterGrid_DilutionPositive_AhgZero_StillRulesOut()
    {
        using var iso = new IsolatedDatabase();
        var previous = AppSettings.Current.ExtraPhases;
        try
        {
            AppSettings.Current.ExtraPhases = ExtraPhaseParser.FromSuggested(
                false, false, false, false, "", titerGrid: true);
            Assert.True(ExtraPhaseParser.ContainsTiterGrid(AppSettings.Current.ExtraPhases));

            iso.Db.AddSpecimen("TITER-CONFLICT", "serum", null);
            var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
            iso.Db.LinkSpecimenPanel("TITER-CONFLICT", panelId);
            var cells = iso.Db.GetPanelCells(panelId);
            iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
            iso.Db.UpdatePanelCellAntigen(cells[0].Id, "e", "-");
            iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "-");
            var runId = iso.Db.GetOrCreateDefaultRun("TITER-CONFLICT", panelId);
            iso.Db.SaveReaction(runId, "1", "0", "0", "0", "2+", new Dictionary<string, string>
            {
                ["Dil1"] = "1+",
                ["Dil2"] = "w+",
                ["Dil4"] = "0"
            });
            iso.Db.SaveReaction(runId, "2", "0", "0", "0", "2+");

            var loaded = iso.Db.GetReactions(runId).Single(r => r.CellNumber == "1");
            Assert.Equal(2, TiterParser.Highest(null, loaded.ExtraPhases));
            var ctx = new RunContext(new PanelRun());
            Assert.True(ctx.IsNegative(loaded));
            Assert.False(ctx.IsPositive(loaded));

            var result = iso.Analyzer.AnalyzeSpecimen("TITER-CONFLICT", updateDb: false);
            Assert.True(result.RuledOut.ContainsKey("anti-E"));
            Assert.Contains(result.RuleoutEvaluations,
                e => e.Antibody == "anti-E" && e.MeetsCriteria);
            Assert.DoesNotContain(result.Suggestions,
                s => s.Contains("identified", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            AppSettings.Current.ExtraPhases = previous;
        }
    }

    private static AnalysisResult HtlaResult()
    {
        var cells = Enumerable.Range(1, 8)
            .Select(i => ReactionPatternClassifier.Observe(new Reaction
            {
                CellNumber = i.ToString(),
                IS = "0",
                C37 = "0",
                AHG = "w+",
                CC = "NT"
            }));
        return new AnalysisResult
        {
            ReactionPatterns = ReactionPatternClassifier.Classify(cells)
        };
    }
}
