using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

public class ReactionSlashTests
{
    [Fact]
    public void SavedNegative_SlashesHomozygousPlus_NotHeterozygous()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"), ("Fya", "+"), ("Fyb", "+"));
        var row = Row(cell, NegativeReaction());

        Assert.Contains("K", row.SlashedAntigens);
        Assert.DoesNotContain("k", row.SlashedAntigens);
        Assert.DoesNotContain("Fya", row.SlashedAntigens);
        Assert.DoesNotContain("Fyb", row.SlashedAntigens);
        Assert.Contains("anti-K", row.RuledOutNote);
        Assert.DoesNotContain("anti-Fya", row.RuledOutNote);
    }

    [Fact]
    public void SavedNegative_SlashesUnpairedPlus()
    {
        var cell = Cell("2", ("D", "+"), ("K", "+"), ("k", "-"));
        var row = Row(cell, NegativeReaction());

        Assert.Contains("D", row.SlashedAntigens);
        Assert.Contains("K", row.SlashedAntigens);
    }

    [Fact]
    public void HetOkRule_SlashesHeterozygousPlus()
    {
        var cell = Cell("2", ("C", "+"), ("c", "+"), ("K", "+"), ("k", "+"));
        var rules = new[]
        {
            new Rule { Name = "C het", Antibody = "anti-C", ExceptionAntigen = "C", HeterozygousOk = true }
        };
        var row = Row(cell, NegativeReaction(), rules);

        Assert.Contains("C", row.SlashedAntigens);
        Assert.DoesNotContain("c", row.SlashedAntigens);
        Assert.DoesNotContain("K", row.SlashedAntigens);
    }

    [Fact]
    public void PositiveRow_HasNoSlashes()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"), ("D", "+"));
        var row = Row(cell, new Reaction { IS = "0", C37 = "0", AHG = "3+", CC = "NT" });

        Assert.Empty(row.SlashedAntigens);
        Assert.False(row.HasRuleout);
    }

    [Fact]
    public void IncompleteRow_HasNoSlashes()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"));
        var row = Row(cell, existing: null);

        Assert.False(row.IsNegative);
        Assert.Empty(row.SlashedAntigens);
    }

    [Fact]
    public void Autocontrol_HasNoSlashes()
    {
        var cell = Cell("AC", ("K", "+"), ("k", "-"), ("D", "+"));
        var row = Row(cell, NegativeReaction());

        Assert.True(row.IsNegative);
        Assert.Empty(row.SlashedAntigens);
        Assert.Equal(string.Empty, row.RuledOutNote);
    }

    [Fact]
    public void DestroyedAntigen_IsNotSlashed()
    {
        var cell = Cell("2", ("Fya", "+"), ("Fyb", "-"), ("K", "+"), ("k", "-"));
        var ctx = new RunContext(new PanelRun { CellTreatment = CellTreatment.Ficin });
        var row = new ReactionRow(cell, NegativeReaction(), Array.Empty<Rule>(), ctx);

        Assert.DoesNotContain("Fya", row.SlashedAntigens);
        Assert.Contains("K", row.SlashedAntigens);
    }

    [Fact]
    public void LoadingSavedZero_BackfillsSlashes()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"), ("Jka", "+"), ("Jkb", "-"));
        var saved = new Reaction { IS = "0", C37 = "0", AHG = "0", CC = "2+" };
        var row = Row(cell, saved);

        Assert.True(row.IsNegative);
        Assert.Contains("K", row.SlashedAntigens);
        Assert.Contains("Jka", row.SlashedAntigens);
    }

    [Fact]
    public void EditingGradesToNegative_AddsSlashesLive()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"));
        var row = Row(cell, existing: null);
        Assert.Empty(row.SlashedAntigens);

        row.IS = "0";
        row.C37 = "0";
        row.AHG = "0";

        Assert.Contains("K", row.SlashedAntigens);
        Assert.True(row.HasRuleout);
    }

    [Fact]
    public void CorrectingAhgToPositive_RemovesSlashes()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"), ("D", "+"));
        var row = Row(cell, NegativeReaction());
        Assert.Contains("K", row.SlashedAntigens);
        Assert.Contains("D", row.SlashedAntigens);

        row.AHG = "2+";

        Assert.False(row.IsNegative);
        Assert.Empty(row.SlashedAntigens);
        Assert.False(row.HasRuleout);
        Assert.Equal(string.Empty, row.RuledOutNote);
    }

    [Fact]
    public void CorrectingIsToPositive_RemovesSlashes()
    {
        var cell = Cell("2", ("K", "+"), ("k", "-"));
        var row = Row(cell, NegativeReaction());
        Assert.Contains("K", row.SlashedAntigens);

        row.IS = "1+";

        Assert.False(row.IsNegative);
        Assert.Empty(row.SlashedAntigens);
        Assert.False(row.HasRuleout);
    }

    [Fact]
    public void UnknownZygosity_IsNotSlashed()
    {
        var cell = Cell("2", ("K", "+"));
        var row = Row(cell, NegativeReaction());

        Assert.DoesNotContain("K", row.SlashedAntigens);
    }

    private static PanelCell Cell(string number, params (string Antigen, string Value)[] antigens)
    {
        var cell = new PanelCell { CellNumber = number };
        foreach (var (ag, value) in antigens)
            cell.SetAntigen(ag, value);
        return cell;
    }

    private static Reaction NegativeReaction() =>
        new() { IS = "0", C37 = "0", AHG = "0", CC = "2+" };

    private static ReactionRow Row(PanelCell cell, Reaction? existing, IReadOnlyList<Rule>? rules = null)
    {
        var ctx = new RunContext(new PanelRun());
        return new ReactionRow(cell, existing, rules ?? Array.Empty<Rule>(), ctx);
    }
}
