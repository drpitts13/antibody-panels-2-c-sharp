using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class SelectedCellRecommendationTests
{
    [Fact]
    public void DistinguishingCell_RanksAboveDoublePositive_AndExplainsWhy()
    {
        var tested = new[] { (1, "1"), (1, "2") };
        var used = new Panel { PanelId = 1, Name = "Used panel", LotNumber = "U1" };
        var stock = new Panel { PanelId = 2, Name = "Stock panel", LotNumber = "S1" };
        var inventory = new (Panel, PanelCell)[]
        {
            (used, Cell("1", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"))),
            (used, Cell("2", ("E", "-"), ("e", "+"), ("K", "+"), ("k", "-"))),
            (stock, Cell("3", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"))),
            (stock, Cell("4", ("E", "+"), ("e", "+"), ("K", "+"), ("k", "-"))),
            (stock, Cell("5", ("E", "-"), ("e", "+"), ("K", "+"), ("k", "-"))),
        };
        var result = TwoCandidateResult();

        var recs = SelectedCellRecommender.Recommend(result, tested, inventory);

        Assert.NotEmpty(recs);
        Assert.DoesNotContain(recs, r => r.PanelId == 1);
        Assert.Contains(recs, r => r.CellNumber == "3");
        var best = recs[0];
        Assert.Contains(best.CellNumber, new[] { "3", "5" });
        Assert.Contains("distinguish", best.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(best.Distinguishes, d => d.Contains("anti-E") && d.Contains("anti-K"));
        var doublePos = recs.FirstOrDefault(r => r.CellNumber == "4");
        if (doublePos != null)
            Assert.True(best.Score > doublePos.Score);
    }

    [Fact]
    public void AlreadyTestedCell_IsNotRecommended()
    {
        var stock = new Panel { PanelId = 2, Name = "Stock", LotNumber = "S" };
        var cell = Cell("7", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"));
        var result = TwoCandidateResult();

        var recs = SelectedCellRecommender.Recommend(
            result,
            new[] { (2, "7") },
            new[] { (stock, cell) });

        Assert.Empty(recs);
    }

    [Fact]
    public void SingleIncompleteAntiE_PrefersUnusedHomozygousE()
    {
        var stock = new Panel { PanelId = 9, Name = "Select", LotNumber = "SEL" };
        var inventory = new (Panel, PanelCell)[]
        {
            (stock, Cell("1", ("E", "+"), ("e", "+"))),
            (stock, Cell("2", ("E", "+"), ("e", "-"))),
            (stock, Cell("3", ("E", "-"), ("e", "+"))),
        };
        var result = new AnalysisResult
        {
            Suspected = { ["anti-E"] = 0.8 },
            SuspectedStatistics =
            {
                ["anti-E"] = new SuspectedStatistics
                {
                    PositiveAgPositiveCount = 1,
                    NegativeAgNegativeCount = 1,
                    IdentificationRequired = 3,
                    MeetsIdentificationRule = false
                }
            }
        };

        var recs = SelectedCellRecommender.Recommend(result, Array.Empty<(int, string)>(), inventory);
        Assert.NotEmpty(recs);
        Assert.Equal("2", recs[0].CellNumber);
        Assert.Contains("homozygous", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnusedSelectedCellVial_RanksAboveMatchingScreeningCell()
    {
        var screen = new Panel { PanelId = 1, Name = "ID Panel", LotNumber = "ID" };
        var select = new Panel { PanelId = 2, Name = "Immucor Selectogen", LotNumber = "SG" };
        var inventory = new (Panel, PanelCell)[]
        {
            (screen, Cell("8", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"))),
            (select, Cell("3", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"))),
        };

        Assert.True(SelectedCellRecommender.LooksLikeSelectedCellVial(select));
        Assert.False(SelectedCellRecommender.LooksLikeSelectedCellVial(screen));

        var recs = SelectedCellRecommender.Recommend(TwoCandidateResult(), Array.Empty<(int, string)>(), inventory);
        Assert.Equal(2, recs.Count);
        Assert.Equal("3", recs[0].CellNumber);
        Assert.Equal(select.PanelId, recs[0].PanelId);
        Assert.True(recs[0].Score > recs[1].Score);
        Assert.Contains("selected-cell vial", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatientENegative_PrefersUnusedEPositiveCell_AndDoesNotAutoIdentify()
    {
        var stock = new Panel { PanelId = 4, Name = "ID Panel", LotNumber = "P" };
        var inventory = new (Panel, PanelCell)[]
        {
            (stock, Cell("1", ("E", "+"), ("e", "-"))),
            (stock, Cell("2", ("E", "-"), ("e", "+"))),
        };
        var result = new AnalysisResult
        {
            Suspected = { ["anti-E"] = 0.8 },
            SuspectedStatistics =
            {
                ["anti-E"] = new SuspectedStatistics
                {
                    PositiveAgPositiveCount = 1,
                    NegativeAgNegativeCount = 1,
                    IdentificationRequired = 3,
                    MeetsIdentificationRule = false
                }
            },
            PatientTypingConsiderations =
            {
                new PatientTypingConsideration
                {
                    Antibody = "anti-E",
                    Antigen = "E",
                    Kind = PatientTypingKind.Supporting,
                    PatientValue = "-",
                    Explanation = "Patient types E-."
                }
            }
        };

        var recs = SelectedCellRecommender.Recommend(result, Array.Empty<(int, string)>(), inventory);
        Assert.Equal("1", recs[0].CellNumber);
        Assert.Contains("patient types E-", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PredictedENegativeAfterTransfusion_StillBoostsEPositiveCell()
    {
        var stock = new Panel { PanelId = 5, Name = "0.8% Selected Cells", LotNumber = "SC" };
        var result = new AnalysisResult
        {
            PatientPhenotypeUnreliable = true,
            Suspected = { ["anti-E"] = 0.7 },
            SuspectedStatistics =
            {
                ["anti-E"] = new SuspectedStatistics
                {
                    PositiveAgPositiveCount = 1,
                    NegativeAgNegativeCount = 0,
                    IdentificationRequired = 3,
                    MeetsIdentificationRule = false
                }
            },
            PatientTypingConsiderations =
            {
                new PatientTypingConsideration
                {
                    Antibody = "anti-E",
                    Antigen = "E",
                    Kind = PatientTypingKind.Uninterpretable,
                    PatientValue = "+",
                    Explanation = "Serology uninterpretable."
                },
                new PatientTypingConsideration
                {
                    Antibody = "anti-E",
                    Antigen = "E",
                    Kind = PatientTypingKind.Predicted,
                    PatientValue = "-",
                    Explanation = "Genotype predicts E-."
                }
            }
        };

        Assert.Equal("-", SelectedCellRecommender.PatientAntigen(result, "E"));
        var recs = SelectedCellRecommender.Recommend(
            result,
            Array.Empty<(int, string)>(),
            new[] { (stock, Cell("4", ("E", "+"), ("e", "-"))) });
        Assert.Single(recs);
        Assert.Contains("patient types E-", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected-cell vial", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatientEPositive_DoesNotRemoveDistinguishingCell()
    {
        var stock = new Panel { PanelId = 6, Name = "Stock panel", LotNumber = "S" };
        var result = TwoCandidateResult();
        result.PatientTypingConsiderations.Add(new PatientTypingConsideration
        {
            Antibody = "anti-E",
            Antigen = "E",
            Kind = PatientTypingKind.Against,
            PatientValue = "+",
            Explanation = "Patient types E+."
        });

        var recs = SelectedCellRecommender.Recommend(
            result,
            Array.Empty<(int, string)>(),
            new[] { (stock, Cell("3", ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"))) });
        Assert.Single(recs);
        Assert.Contains("distinguish", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weaker evidence", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyzer_IncludesSelectedCellsForMultipleAntibodies()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("SEL-MULTI", "serum", null);
        var usedId = iso.Db.AddPanel("Used", "U", "V", 4, null, false);
        var stockId = iso.Db.AddPanel("Stock", "S", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("SEL-MULTI", usedId);
        var used = iso.Db.GetPanelCells(usedId);
        Set(iso, used[0], ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"));
        Set(iso, used[1], ("E", "-"), ("e", "+"), ("K", "+"), ("k", "-"));
        Set(iso, used[2], ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"));
        Set(iso, used[3], ("E", "-"), ("e", "+"), ("K", "+"), ("k", "-"));
        iso.Db.SaveReaction("SEL-MULTI", usedId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("SEL-MULTI", usedId, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction("SEL-MULTI", usedId, "3", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("SEL-MULTI", usedId, "4", "0", "0", "2+", "NT");
        var stock = iso.Db.GetPanelCells(stockId);
        Set(iso, stock[0], ("E", "+"), ("e", "-"), ("K", "-"), ("k", "+"));
        Set(iso, stock[1], ("E", "+"), ("e", "+"), ("K", "+"), ("k", "-"));

        var result = iso.Analyzer.AnalyzeSpecimen("SEL-MULTI", updateDb: false);
        Assert.True(
            result.Suspected.ContainsKey("anti-E") ||
            result.Suspected.ContainsKey("anti-K") ||
            result.PatternMatches.Any(p => p.Antibody is "anti-E" or "anti-K"),
            $"Expected E or K as a remaining candidate. Suspected: {string.Join(", ", result.Suspected.Keys)}");
        Assert.NotEmpty(result.SelectedCellRecommendations);
        Assert.Contains(result.SelectedCellRecommendations, r => r.PanelName == "Stock");
        Assert.DoesNotContain(result.SelectedCellRecommendations, r => r.PanelId == usedId);
        Assert.Contains(result.Suggestions, s => s.Contains("Selected cells", StringComparison.OrdinalIgnoreCase));
    }

    private static AnalysisResult TwoCandidateResult() => new()
    {
        Suspected = { ["anti-E"] = 0.8, ["anti-K"] = 0.7 },
        SuspectedStatistics =
        {
            ["anti-E"] = new SuspectedStatistics
            {
                PositiveAgPositiveCount = 1,
                NegativeAgNegativeCount = 0,
                IdentificationRequired = 3,
                MeetsIdentificationRule = false
            },
            ["anti-K"] = new SuspectedStatistics
            {
                PositiveAgPositiveCount = 1,
                NegativeAgNegativeCount = 0,
                IdentificationRequired = 3,
                MeetsIdentificationRule = false
            }
        }
    };

    private static PanelCell Cell(string number, params (string Ag, string Val)[] antigens)
    {
        var cell = new PanelCell { CellNumber = number };
        foreach (var (ag, val) in antigens)
            cell.SetAntigen(ag, val);
        return cell;
    }

    private static void Set(IsolatedDatabase iso, PanelCell cell, params (string Ag, string Val)[] antigens)
    {
        foreach (var (ag, val) in antigens)
            iso.Db.UpdatePanelCellAntigen(cell.Id, ag, val);
    }
}
