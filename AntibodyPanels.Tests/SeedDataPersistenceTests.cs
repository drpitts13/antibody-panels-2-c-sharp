using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class SeedDataPersistenceTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public SeedDataPersistenceTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void SeededDatabase_ContainsExpectedPanels()
    {
        var panels = _fixture.Db.GetAllPanels();
        Assert.Equal(5, panels.Count);
        Assert.Contains(panels, p => p.Name == "Ortho Resolve Panel A" && p.LotNumber == "ORT2024A");
        Assert.Contains(panels, p => p.Vendor == "Immucor");
        Assert.Contains(panels, p => p.Vendor == "Bio-Rad");
        Assert.Contains(panels, p => p.Vendor == "Quotient");
        Assert.Contains(panels, p => p.Vendor == "Grifols");
    }

    [Fact]
    public void SeededDatabase_ContainsExpectedSpecimens()
    {
        var specimens = _fixture.Db.GetAllSpecimens();
        Assert.True(specimens.Count >= 17);
        Assert.Contains(specimens, s => s.AccessionNumber == "2024-001" && s.Type == "serum");
        Assert.Contains(specimens, s => s.AccessionNumber == "2024-005" && s.Type == "eluate");
        Assert.Contains(specimens, s => s.AccessionNumber == "TEST-NO-AB");
        Assert.Contains(specimens, s => s.AccessionNumber == "TEST-MULTI-AB");
    }

    [Fact]
    public void SeededDatabase_ContainsRulesAndCompletePanelCells()
    {
        Assert.Equal(3, _fixture.Db.GetAllRules().Count);

        var ortho = _fixture.Db.GetAllPanels().First(p => p.Name == "Ortho Resolve Panel A");
        var cells = _fixture.Db.GetPanelCells(ortho.PanelId);
        Assert.Equal(12, cells.Count); // 11 cells + AC
        foreach (var cell in cells.Where(c => c.CellNumber != "AC"))
        {
            foreach (var ag in AntigenConstants.Antigens)
                Assert.True(cell.GetAntigen(ag) is "+" or "-");
        }
    }

    [Fact]
    public void SeededDatabase_PersistsOnDiskAfterTests()
    {
        Assert.True(File.Exists(_fixture.DbPath));
        Assert.True(File.Exists(_fixture.DbPath + TestDataSeeder.SeedMarkerFile));
    }
}
