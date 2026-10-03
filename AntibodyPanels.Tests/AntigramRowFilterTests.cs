using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Tests;

public class AntigramRowFilterTests
{
    [Fact]
    public void HomozygousFilter_KeepsOnlyHomoEPlus_AndAlwaysShowsAc()
    {
        var rows = new[]
        {
            Row("1", false, E: "+", e: "-"),
            Row("2", true, E: "+", e: "+"),
            Row("3", false, E: "-"),
            Row("AC", false, E: "+")
        };

        var visible = AntigramRowFilter.Apply(rows, "E", AntigenConstants.ZygosityHomozygous,
            AntigramRowFilter.SortCellNumber);

        Assert.Equal(new[] { "1", "AC" }, visible.Select(r => r.CellNumber));
        Assert.Contains("homozygous E+", AntigramRowFilter.Explain(visible, rows.Length, "E",
            AntigenConstants.ZygosityHomozygous, AntigramRowFilter.SortCellNumber));
        Assert.Contains("autocontrol always shown", AntigramRowFilter.Explain(visible, rows.Length, "E",
            AntigenConstants.ZygosityHomozygous, AntigramRowFilter.SortCellNumber));
    }

    [Fact]
    public void UntypedE_IsNotTreatedAsNegativeMatch()
    {
        var rows = new[]
        {
            Row("1", false),
            Row("2", false, E: "+")
        };

        var visible = AntigramRowFilter.Apply(rows, "E", AntigenConstants.ZygosityBoth,
            AntigramRowFilter.SortCellNumber);

        Assert.Equal(new[] { "2" }, visible.Select(r => r.CellNumber));
        Assert.DoesNotContain(visible, r => r.CellNumber == "1");
    }

    [Fact]
    public void ReactiveFirst_OrdersPositiveGradesAheadOfNegative()
    {
        var rows = new[]
        {
            Row("1", false, E: "+"),
            Row("2", true, E: "+"),
            Row("3", false, E: "+")
        };

        var visible = AntigramRowFilter.Apply(rows, "All cells", AntigenConstants.ZygosityBoth,
            AntigramRowFilter.SortReactiveFirst);

        Assert.Equal(new[] { "2", "1", "3" }, visible.Select(r => r.CellNumber));
        Assert.Contains("reactive first", AntigramRowFilter.Explain(visible, rows.Length,
            AntigramRowFilter.AllCells, AntigenConstants.ZygosityBoth,
            AntigramRowFilter.SortReactiveFirst));
    }

    [Fact]
    public void AntigenFirst_PutsHomozygousAheadOfHeterozygous()
    {
        var rows = new[]
        {
            Row("1", false, E: "+", e: "+"),
            Row("2", false, E: "+", e: "-"),
            Row("3", false, E: "+")
        };

        var visible = AntigramRowFilter.Apply(rows, "E", AntigenConstants.ZygosityBoth,
            AntigramRowFilter.SortAntigenFirst);

        Assert.Equal(new[] { "2", "1", "3" }, visible.Select(r => r.CellNumber));
    }

    private static AntigramFilterRow Row(string cell, bool reactive, string? E = null, string? e = null)
    {
        var antigens = new Dictionary<string, string>();
        if (E != null) antigens["E"] = E;
        if (e != null) antigens["e"] = e;
        return new AntigramFilterRow(cell, antigens, reactive);
    }
}
