using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Tests;

public class PanelCompareTests
{
    [Fact]
    public void TypingChange_AndUntypedPartner_AreListedWithoutInventingNegatives()
    {
        var left = new[]
        {
            Cell("1", ("E", "+"), ("e", "-"), ("K", "+")),
            Cell("2", ("E", "-"), ("e", "+"))
        };
        var right = new[]
        {
            Cell("1", ("E", "-"), ("e", "+"), ("Fya", "+")),
            Cell("2", ("E", "-"), ("e", "+")),
            Cell("3", ("E", "+"))
        };

        var result = PanelComparer.Compare(
            "Lot A", left, new[] { "E", "e", "K" },
            "Lot B", right, new[] { "E", "e", "Fya" });

        Assert.True(result.HasDifferences);
        Assert.Contains("Fya", result.Schema.Added);
        Assert.Contains("K", result.Schema.Removed);
        Assert.Equal(new[] { "3" }, result.OnlyRightCells);
        Assert.Empty(result.OnlyLeftCells);

        var e1 = result.TypingDiffs.Single(d => d.CellNumber == "1" && d.Antigen == "E");
        Assert.Equal("+", e1.Left);
        Assert.Equal("-", e1.Right);
        Assert.Contains("Cell 1 E is + on the selected panel and - on the comparison panel", e1.Note);

        var k1 = result.TypingDiffs.Single(d => d.CellNumber == "1" && d.Antigen == "K");
        Assert.Equal("+", k1.Left);
        Assert.Equal("NT", k1.Right);
        Assert.DoesNotContain(result.TypingDiffs, d => d.Antigen == "K" && d.Right == "-");

        Assert.Contains("Added on the comparison panel: Fya", result.Explanation);
        Assert.Contains("Missing from the comparison panel: K", result.Explanation);
        Assert.Contains("Cells only on the comparison panel: 3", result.Explanation);
    }

    [Fact]
    public void IdenticalTypings_Match()
    {
        var cells = new[] { Cell("1", ("D", "+"), ("C", "+")) };
        var result = PanelComparer.Compare("A", cells, new[] { "D", "C" }, "B", cells, new[] { "D", "C" });
        Assert.False(result.HasDifferences);
        Assert.Contains("typed cell values match", result.Explanation);
    }

    [Fact]
    public void MissingAntigenOnOneCell_IsNtNotNegative()
    {
        var left = new[] { Cell("1", ("E", "+")) };
        var right = new[] { Cell("1") };
        var result = PanelComparer.Compare("A", left, new[] { "E" }, "B", right, new[] { "E" });
        var diff = Assert.Single(result.TypingDiffs);
        Assert.Equal("NT", diff.Right);
        Assert.Equal("+", diff.Left);
        Assert.False(right[0].HasTypedAntigen("E"));
        Assert.NotEqual("-", diff.Right);
    }

    private static PanelCell Cell(string number, params (string Ag, string Val)[] antigens)
    {
        var cell = new PanelCell { CellNumber = number };
        foreach (var (ag, val) in antigens)
            cell.SetAntigen(ag, val);
        return cell;
    }
}
