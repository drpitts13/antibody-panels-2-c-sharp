using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

public class CompareGridTests
{
    [Fact]
    public void ExtraPhaseOnlyDifference_IsVisibleAndExplained()
    {
        var row = CompareReactionRow.Build(
            "1",
            "0", "0", "0", "2+",
            "0", "0", "0", "2+",
            new Dictionary<string, string> { ["RT"] = "2+" },
            new Dictionary<string, string> { ["RT"] = "0" },
            new[] { "RT", "PEG" });

        Assert.True(row.AnyChanged);
        Assert.True(row.ExtraChanged("RT"));
        Assert.False(row.ExtraChanged("PEG"));
        Assert.Equal("2+", row.GetLeftExtra("RT"));
        Assert.Equal("0", row.GetRightExtra("RT"));
        Assert.Equal("NT", row.GetLeftExtra("PEG"));
        Assert.Contains("RT (this 2+ vs other 0)", row.Explanation);
        Assert.DoesNotContain("AHG", row.Explanation);
        Assert.DoesNotContain("PEG", row.Explanation);
    }

    [Fact]
    public void MatchingExtraPhases_DoNotFlagChange()
    {
        var row = CompareReactionRow.Build(
            "2",
            "0", "0", "0", "2+",
            "0", "0", "0", "2+",
            new Dictionary<string, string> { ["RT"] = "0", ["Gel"] = "0" },
            new Dictionary<string, string> { ["RT"] = "0", ["Gel"] = "0" },
            new[] { "RT", "Gel" });

        Assert.False(row.AnyChanged);
        Assert.Equal("Cell 2 matches on all compared phases.", row.Explanation);
    }

    [Fact]
    public void MissingExtraOnOtherRun_CountsAsNtDifference()
    {
        var row = CompareReactionRow.Build(
            "3",
            "0", "0", "0", "2+",
            "0", "0", "0", "2+",
            new Dictionary<string, string> { ["PEG"] = "2+" },
            null,
            new[] { "PEG" });

        Assert.True(row.AnyChanged);
        Assert.True(row.ExtraChanged("PEG"));
        Assert.Equal("NT", row.GetRightExtra("PEG"));
        Assert.Contains("PEG (this 2+ vs other NT)", row.Explanation);
    }

    [Fact]
    public void AhgDifference_StillExplainedWhenExtrasMatch()
    {
        var row = CompareReactionRow.Build(
            "4",
            "0", "0", "2+", "NT",
            "0", "0", "0", "2+",
            new Dictionary<string, string> { ["RT"] = "0" },
            new Dictionary<string, string> { ["RT"] = "0" },
            new[] { "RT" });

        Assert.True(row.AnyChanged);
        Assert.True(row.AhgChanged);
        Assert.False(row.ExtraChanged("RT"));
        Assert.Contains("AHG (this 2+ vs other 0)", row.Explanation);
        Assert.DoesNotContain("RT", row.Explanation);
    }
}
