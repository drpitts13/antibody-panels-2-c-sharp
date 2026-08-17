using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class PanelAdministrationTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public PanelAdministrationTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAllPanels_ReturnsSeededPanelsSortedByName()
    {
        var names = _fixture.Db.GetAllPanels().Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public void AddPanel_CreatesCellsWithDefaultAntigens()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Admin Test Panel", "LOT-99", "Test Vendor", 5, "2030-01-01", includeAc: true);
        var panel = iso.Db.GetPanel(panelId);
        Assert.NotNull(panel);
        Assert.Equal(5, panel!.NumCells);
        Assert.True(panel.IncludeAc);

        var cells = iso.Db.GetPanelCells(panelId);
        Assert.Equal(6, cells.Count);
        foreach (var cell in cells)
            foreach (var ag in AntigenConstants.Antigens)
                Assert.Equal("-", cell.GetAntigen(ag));
    }

    [Fact]
    public void UpdatePanel_RebuildsCellsWhenCellCountChanges()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Resize Panel", "L1", "V", 3, null, false);
        iso.Db.UpdatePanel(panelId, "Resize Panel", "L1", "V", 5, null, false);
        Assert.Equal(5, iso.Db.GetPanelCells(panelId).Count(c => c.CellNumber != "AC"));
    }

    [Fact]
    public void UpdatePanelCellAntigen_PersistsAntigenProfile()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Antigen Panel", "L1", "V", 1, null, false);
        var cell = iso.Db.GetPanelCells(panelId).Single();
        iso.Db.UpdatePanelCellAntigen(cell.Id, "D", "+");
        iso.Db.UpdatePanelCellAntigen(cell.Id, "E", "-");

        var updated = iso.Db.GetPanelCells(panelId).Single();
        Assert.Equal("+", updated.GetAntigen("D"));
        Assert.Equal("-", updated.GetAntigen("E"));
    }

    [Fact]
    public void CopyPanelCells_CopiesFullAntigenProfile()
    {
        using var iso = new IsolatedDatabase();
        var sourceId = iso.Db.AddPanel("Source", "L1", "V", 2, null, false);
        var targetId = iso.Db.AddPanel("Target", "L2", "V", 2, null, false);
        var sourceCell = iso.Db.GetPanelCells(sourceId).First();
        iso.Db.UpdatePanelCellAntigen(sourceCell.Id, "K", "+");

        iso.Db.CopyPanelCells(sourceId, targetId);
        var copied = iso.Db.GetPanelCells(targetId).First(c => c.CellNumber == sourceCell.CellNumber);
        Assert.Equal("+", copied.GetAntigen("K"));
    }

    [Fact]
    public void DeletePanel_CascadesToCellsAndLinks()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DEL-PANEL-SPEC", "serum", null);
        var panelId = iso.Db.AddPanel("Delete Me", "L1", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("DEL-PANEL-SPEC", panelId);
        iso.Db.DeletePanel(panelId);

        Assert.Null(iso.Db.GetPanel(panelId));
        Assert.Empty(iso.Db.GetSpecimenPanels("DEL-PANEL-SPEC"));
    }

    [Fact]
    public void SeededOrthoPanel_HasCorrectMetadataAndAcRow()
    {
        var ortho = _fixture.Db.GetAllPanels().First(p => p.LotNumber == "ORT2024A");
        Assert.Equal("Ortho Clinical Diagnostics", ortho.Vendor);
        Assert.True(ortho.IncludeAc);
        Assert.Contains(_fixture.Db.GetPanelCells(ortho.PanelId), c => c.CellNumber == "AC");
    }

    [Fact]
    public void PanelCellRow_ToggleAntigen_SwitchesPlusAndMinus()
    {
        var cell = new PanelCell { CellNumber = "1" };
        cell.SetAntigen("E", "+");
        var row = new PanelCellRow(cell);

        row.ToggleAntigen("E");
        Assert.Equal("-", row.E);
        row.ToggleAntigen("E");
        Assert.Equal("+", row.E);
    }
}
