using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class PanelAntigenOrderTests
{
    [Fact]
    public void GetPanelAntigenOrder_Empty_WhenNeverSaved()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Default", "L", "V", 1, null, false);
        Assert.Empty(iso.Db.GetPanelAntigenOrder(id));
        Assert.Equal(AntigenConstants.Antigens, iso.Db.GetPanelDisplayAntigens(id));
    }

    [Fact]
    public void SetPanelAntigenOrder_RoundTripsCustomOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Custom", "L", "V", 1, null, false);
        var custom = AntigenConstants.Antigens.Reverse().ToList();
        iso.Db.SetPanelAntigenOrder(id, custom);
        Assert.Equal(custom, iso.Db.GetPanelAntigenOrder(id));
        Assert.Equal(custom, iso.Db.GetPanelDisplayAntigens(id));
    }

    [Fact]
    public void ResolveDisplayOrder_EmptySaved_UsesCanonicalDefault()
    {
        var extras = new[] { "Doa", "Vel" };
        Assert.Equal(
            AntigenConstants.GetAnalyzedAntigens(extras),
            AntigenConstants.ResolveDisplayOrder(Array.Empty<string>(), extras));
        Assert.Equal(
            AntigenConstants.GetAnalyzedAntigens(extras),
            AntigenConstants.ResolveDisplayOrder(null, extras));
    }

    [Fact]
    public void ResolveDisplayOrder_AppendsNewExtra_AndDropsRemoved()
    {
        var saved = AntigenConstants.Antigens.ToList();
        saved.Insert(1, "Doa");

        var withDob = AntigenConstants.ResolveDisplayOrder(saved, new[] { "Doa", "Dob" });
        Assert.Equal("D", withDob[0]);
        Assert.Equal("Doa", withDob[1]);
        Assert.Equal("Dob", withDob[^1]);

        var withoutDoa = AntigenConstants.ResolveDisplayOrder(saved, new[] { "Dob" });
        Assert.DoesNotContain("Doa", withoutDoa);
        Assert.Contains("Dob", withoutDoa);
        Assert.Equal("D", withoutDoa[0]);
    }

    [Fact]
    public void AddPanelExtraAntigen_AppendsToExistingCustomOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Extra", "L", "V", 1, null, false);
        var custom = AntigenConstants.Antigens.Reverse().ToList();
        iso.Db.SetPanelAntigenOrder(id, custom);
        iso.Db.AddPanelExtraAntigen(id, "Doa");

        var order = iso.Db.GetPanelDisplayAntigens(id);
        Assert.Equal(custom[0], order[0]);
        Assert.Equal("Doa", order[^1]);
    }

    [Fact]
    public void RemovePanelExtraAntigen_DropsFromCustomOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Remove", "L", "V", 1, null, false);
        iso.Db.AddPanelExtraAntigen(id, "Doa");
        iso.Db.AddPanelExtraAntigen(id, "Dob");
        var custom = AntigenConstants.Antigens.ToList();
        custom.Insert(2, "Doa");
        custom.Add("Dob");
        iso.Db.SetPanelAntigenOrder(id, custom);

        iso.Db.RemovePanelExtraAntigen(id, "Doa");
        var order = iso.Db.GetPanelDisplayAntigens(id);
        Assert.DoesNotContain("Doa", order);
        Assert.Contains("Dob", order);
        Assert.Equal("D", order[0]);
        Assert.Equal("C", order[1]);
    }

    [Fact]
    public void CopyPanelCells_CopiesAntigenOrder()
    {
        using var iso = new IsolatedDatabase();
        var source = iso.Db.AddPanel("Order Src", "L1", "V", 2, null, false);
        iso.Db.AddPanelExtraAntigen(source, "Doa");
        var custom = AntigenConstants.Antigens.ToList();
        custom.Insert(1, "Doa");
        iso.Db.SetPanelAntigenOrder(source, custom);

        var target = iso.Db.AddPanel("Order Tgt", "L2", "V", 2, null, false);
        iso.Db.CopyPanelCells(source, target);

        Assert.Equal(iso.Db.GetPanelAntigenOrder(source), iso.Db.GetPanelAntigenOrder(target));
        Assert.Equal(iso.Db.GetPanelDisplayAntigens(source), iso.Db.GetPanelDisplayAntigens(target));
    }

    [Fact]
    public void DeletePanel_RemovesAntigenOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Delete", "L", "V", 1, null, false);
        iso.Db.SetPanelAntigenOrder(id, AntigenConstants.Antigens.Reverse().ToList());
        iso.Db.DeletePanel(id);

        Assert.Null(iso.Db.GetPanel(id));
        Assert.Empty(iso.Db.GetPanelAntigenOrder(id));
    }

    [Fact]
    public void UpdatePanel_PreservesAntigenOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Order Resize", "L", "V", 3, null, false);
        var custom = AntigenConstants.Antigens.Reverse().ToList();
        iso.Db.SetPanelAntigenOrder(id, custom);
        iso.Db.UpdatePanel(id, "Order Resize", "L", "V", 5, null, false);
        Assert.Equal(custom, iso.Db.GetPanelAntigenOrder(id));
    }

    [Fact]
    public void PanelCsv_ExportUsesSavedOrder()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("CSV Order", "L", "V", 1, null, false);
        iso.Db.AddPanelExtraAntigen(id, "Doa");
        var custom = AntigenConstants.Antigens.ToList();
        custom.Insert(1, "Doa");
        iso.Db.SetPanelAntigenOrder(id, custom);

        var path = Path.Combine(Path.GetTempPath(), $"panel_order_{Guid.NewGuid():N}.csv");
        try
        {
            PanelCsvService.Export(
                iso.Db.GetPanelCells(id),
                path,
                iso.Db.GetPanelAntigenOrder(id));
            var imported = PanelCsvService.Import(path);
            Assert.True(imported.Success, string.Join("; ", imported.Errors));
            Assert.Equal(iso.Db.GetPanelDisplayAntigens(id), imported.AntigenHeaderOrder);

            var newId = iso.Db.AddPanel("CSV Imported", "L2", "V", 1, null, false);
            iso.Db.ReplacePanelCells(newId, imported.Cells.Select(c =>
            {
                var cell = new PanelCell { CellNumber = c.CellNumber };
                foreach (var (ag, val) in c.Antigens)
                    cell.SetAntigen(ag, val);
                return cell;
            }).ToList());
            iso.Db.SetPanelAntigenOrder(newId, imported.AntigenHeaderOrder);
            Assert.Equal(iso.Db.GetPanelDisplayAntigens(id), iso.Db.GetPanelDisplayAntigens(newId));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
