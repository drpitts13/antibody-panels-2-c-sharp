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
