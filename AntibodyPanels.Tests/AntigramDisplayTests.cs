using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Tests;

public class AntigramDisplayTests
{
    [Fact]
    public void HomozygousE_ShowsDoublePlusOnlyWhenDosageOn()
    {
        var antigens = new Dictionary<string, string> { ["E"] = "+", ["e"] = "-" };
        Assert.Equal(AntigenZygosity.Homozygous, AntigramDisplay.Classify(antigens, "E"));
        Assert.Equal("+", AntigramDisplay.Format(antigens, "E", showDosage: false));
        Assert.Equal("++", AntigramDisplay.Format(antigens, "E", showDosage: true));
        Assert.Contains("homozygous for E (E+ e−", AntigramDisplay.Explain("1", antigens, "E"));
    }

    [Fact]
    public void HeterozygousE_StaysSinglePlus()
    {
        var antigens = new Dictionary<string, string> { ["E"] = "+", ["e"] = "+" };
        Assert.Equal(AntigenZygosity.Heterozygous, AntigramDisplay.Classify(antigens, "E"));
        Assert.Equal("+", AntigramDisplay.Format(antigens, "E", showDosage: true));
        Assert.Contains("heterozygous for E (E+ e+", AntigramDisplay.Explain("2", antigens, "E"));
    }

    [Fact]
    public void UntypedPartner_IsUnknownZygosity_NotInventedHomozygous()
    {
        var antigens = new Dictionary<string, string> { ["E"] = "+" };
        Assert.Equal(AntigenZygosity.PositiveUnknown, AntigramDisplay.Classify(antigens, "E"));
        Assert.Equal("+", AntigramDisplay.Format(antigens, "E", showDosage: false));
        Assert.Equal("+/?", AntigramDisplay.Format(antigens, "E", showDosage: true));
        Assert.Contains("e was not typed, so zygosity is unknown", AntigramDisplay.Explain("3", antigens, "E"));
        Assert.False(antigens.ContainsKey("e"));
    }

    [Fact]
    public void DPositive_HasNoAntitheticalMark()
    {
        var antigens = new Dictionary<string, string> { ["D"] = "+" };
        Assert.Equal("+", AntigramDisplay.Format(antigens, "D", showDosage: true));
        Assert.Contains("is D+", AntigramDisplay.Explain("4", antigens, "D"));
        Assert.DoesNotContain("homozygous", AntigramDisplay.Explain("4", antigens, "D"));
    }

    [Fact]
    public void GroupBySystem_KeepsOnlyListedAntigens_RhThenKellThenDuffy()
    {
        var grouped = AntigramDisplay.GroupBySystem(new[] { "Fya", "K", "D", "C", "Doa" });
        Assert.Equal(new[] { "D", "C", "K", "Fya", "Doa" }, grouped);
        Assert.Equal("Rh", AntigramDisplay.SystemOf("D"));
        Assert.Equal("Kell", AntigramDisplay.SystemOf("K"));
        Assert.Equal("Duffy", AntigramDisplay.SystemOf("Fya"));
        Assert.Equal("Dombrock", AntigramDisplay.SystemOf("Doa"));
    }

    [Fact]
    public void NegativeAndMissing_StayUntypedOrNegative()
    {
        var antigens = new Dictionary<string, string> { ["K"] = "-" };
        Assert.Equal("-", AntigramDisplay.Format(antigens, "K", showDosage: true));
        Assert.Equal("", AntigramDisplay.Format(antigens, "k", showDosage: true));
        Assert.Contains("was not typed for k", AntigramDisplay.Explain("5", antigens, "k"));
    }
}
