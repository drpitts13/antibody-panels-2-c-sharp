using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class SearchZygosityTests
{
    [Fact]
    public void Both_FindsHomozygousAndHeterozygousCells()
    {
        using var iso = CreateCcPanel(out var homo, out var het, out var neg);
        var matches = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityBoth);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.cell.CellNumber == homo);
        Assert.Contains(matches, m => m.cell.CellNumber == het);
        Assert.DoesNotContain(matches, m => m.cell.CellNumber == neg);
    }

    [Fact]
    public void Homozygous_FindsOnlyAntigenPositiveAntitheticalNegative()
    {
        using var iso = CreateCcPanel(out var homo, out var het, out _);
        var matches = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityHomozygous);

        Assert.Single(matches);
        Assert.Equal(homo, matches[0].cell.CellNumber);
        Assert.Equal("+", matches[0].cell.GetAntigen("C"));
        Assert.Equal("-", matches[0].cell.GetAntigen("c"));
        Assert.DoesNotContain(matches, m => m.cell.CellNumber == het);
    }

    [Fact]
    public void Heterozygous_FindsOnlyBothAllelesPositive()
    {
        using var iso = CreateCcPanel(out var homo, out var het, out _);
        var matches = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityHeterozygous);

        Assert.Single(matches);
        Assert.Equal(het, matches[0].cell.CellNumber);
        Assert.Equal("+", matches[0].cell.GetAntigen("C"));
        Assert.Equal("+", matches[0].cell.GetAntigen("c"));
        Assert.DoesNotContain(matches, m => m.cell.CellNumber == homo);
    }

    [Fact]
    public void NegativeCriteria_IgnoreZygosity()
    {
        using var iso = CreateCcPanel(out _, out _, out var neg);
        var matches = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "-" },
            AntigenConstants.ZygosityHomozygous);

        Assert.Single(matches);
        Assert.Equal(neg, matches[0].cell.CellNumber);
    }

    [Fact]
    public void UnpairedAntigen_StaysPositiveWhenZygosityIsHomozygous()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("D panel", "L", "V", 2, null, false);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "D", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "D", "-");

        var matches = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["D"] = "+" },
            AntigenConstants.ZygosityHomozygous);

        Assert.Single(matches);
        Assert.Equal("+", matches[0].cell.GetAntigen("D"));
    }

    [Fact]
    public void Warehouse_HomozygousAndHeterozygous()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Do panel", "L", "V", 3, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Doa");
        iso.Db.AddPanelExtraAntigen(panelId, "Dob");
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Doa", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Dob", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Doa", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Dob", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "Doa", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "Dob", "+");

        var both = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityBoth);
        var homo = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityHomozygous);
        var het = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityHeterozygous);

        Assert.Equal(2, both.Count);
        Assert.Single(homo);
        Assert.Equal("+", homo[0].cell.GetAntigen("Doa"));
        Assert.Equal("-", homo[0].cell.GetAntigen("Dob"));
        Assert.Single(het);
        Assert.Equal("+", het[0].cell.GetAntigen("Doa"));
        Assert.Equal("+", het[0].cell.GetAntigen("Dob"));
    }

    [Fact]
    public void Warehouse_UnknownZygosityExcludedFromHomoAndHet()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Doa only", "L", "V", 1, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Doa");
        var cell = iso.Db.GetPanelCells(panelId).Single();
        iso.Db.UpdatePanelCellAntigen(cell.Id, "Doa", "+");

        var both = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityBoth);
        var homo = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityHomozygous);
        var het = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityHeterozygous);

        Assert.Single(both);
        Assert.Empty(homo);
        Assert.Empty(het);
    }

    private static IsolatedDatabase CreateCcPanel(out string homoCell, out string hetCell, out string negCell)
    {
        var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Cc panel", "L", "V", 3, null, false);
        var cells = iso.Db.GetPanelCells(panelId);

        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "C", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "C", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "C", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "+");

        homoCell = cells[0].CellNumber;
        hetCell = cells[1].CellNumber;
        negCell = cells[2].CellNumber;
        return iso;
    }
}
