using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ClinicalDataSeederTests
{
    [Fact]
    public void Seed_CreatesTenSpecimensFivePanelsAndMixedSpecialRuns()
    {
        using var iso = new IsolatedDatabase();
        ClinicalDataSeeder.Seed(iso.Db, iso.Analyzer);

        var specimens = iso.Db.GetAllSpecimens();
        var panels = iso.Db.GetAllPanels();
        Assert.Equal(10, specimens.Count);
        Assert.Equal(5, panels.Count);

        Assert.Contains(specimens, s => s.AccessionNumber == "2026-002");
        Assert.Contains(specimens, s => s.AccessionNumber == "2026-004");
        Assert.Contains(specimens, s => s.AccessionNumber == "2026-010");

        var fyaRuns = iso.Db.GetPanelRuns("2026-002", panels.First(p => p.LotNumber == "ORT2026A").PanelId);
        Assert.Contains(fyaRuns, r => r.CellTreatment == CellTreatment.Ficin);

        var autoRuns = iso.Db.GetPanelRuns("2026-004", panels.First(p => p.LotNumber == "IMM2026B").PanelId);
        Assert.Contains(autoRuns, r => r.SerumTreatment == SerumTreatment.AlloAdsorptionRr);

        var jkaRuns = iso.Db.GetPanelRuns("2026-010", panels.First(p => p.LotNumber == "GRI2026E").PanelId);
        Assert.Contains(jkaRuns, r => r.CellTreatment == CellTreatment.Ficin);

        Assert.NotEmpty(iso.Db.GetSpecimenAntibodies("2026-001"));
        Assert.Empty(iso.Db.GetSpecimenAntibodies("2026-008"));
    }

    [Fact]
    public void SeedIfNeeded_IsIdempotent()
    {
        using var iso = new IsolatedDatabase();
        ClinicalDataSeeder.SeedIfNeeded(iso.Db, iso.Analyzer);
        ClinicalDataSeeder.SeedIfNeeded(iso.Db, iso.Analyzer);
        Assert.Equal(10, iso.Db.GetAllSpecimens().Count);
        Assert.Equal(5, iso.Db.GetAllPanels().Count);
    }
}
