using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class WarehouseAntigenTests
{
    [Fact]
    public void WarehouseCatalog_ContainsExpectedAntigens()
    {
        Assert.Equal(new[] { "Doa", "Dob", "Dia", "Dib", "Wra", "Wrb", "Coa", "Cob", "Yta", "Ytb", "Vel" },
            AntigenConstants.WarehouseAntigens);
        Assert.Equal("Dob", AntigenConstants.AntitheticalPairs["Doa"]);
        Assert.Equal("Doa", AntigenConstants.AntitheticalPairs["Dob"]);
        Assert.False(AntigenConstants.AntitheticalPairs.ContainsKey("Vel"));
    }

    [Fact]
    public void TreatmentEffects_DombrockAndYtDestroyedByDtt_NotFicin()
    {
        foreach (var ag in new[] { "Doa", "Dob", "Yta", "Ytb" })
        {
            Assert.Equal(AntigenEffect.Destroyed,
                AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, ag));
            Assert.Equal(AntigenEffect.Unaffected,
                AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, ag));
        }
    }

    [Fact]
    public void TreatmentEffects_VelEnhancedByFicin()
    {
        Assert.Equal(AntigenEffect.Enhanced,
            AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, "Vel"));
        Assert.Equal(AntigenEffect.Unaffected,
            AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, "Vel"));
    }

    [Fact]
    public void ExtraAntigen_IsAbsentFromUntypedPanels()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Std", "L", "V", 2, null, false);
        var cells = iso.Db.GetPanelCells(panelId);
        Assert.Empty(iso.Db.GetPanelExtraAntigens(panelId));
        foreach (var cell in cells)
            Assert.False(cell.HasTypedAntigen("Doa"));
    }

    [Fact]
    public void AddPanelExtraAntigen_TypesAllCellsDefaultNegative()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Do", "L", "V", 3, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Doa");
        iso.Db.AddPanelExtraAntigen(panelId, "Dob");

        Assert.Equal(new[] { "Doa", "Dob" }, iso.Db.GetPanelExtraAntigens(panelId));
        var cells = iso.Db.GetPanelCells(panelId);
        foreach (var cell in cells)
        {
            Assert.True(cell.HasTypedAntigen("Doa"));
            Assert.True(cell.HasTypedAntigen("Dob"));
            Assert.Equal("-", cell.GetAntigen("Doa"));
            Assert.Equal("-", cell.GetAntigen("Dob"));
        }
    }

    [Fact]
    public void AnalyzeSpecimen_IdentifiesAntiDoaWhenTypedOnRunPanel()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DOA-ID", "serum", null);
        var panelId = CreateDoPanel(iso, 4);
        iso.Db.LinkSpecimenPanel("DOA-ID", panelId);

        var cells = iso.Db.GetPanelCells(panelId);
        SetDo(iso, cells[0], doa: "+", dob: "-");
        SetDo(iso, cells[1], doa: "+", dob: "-");
        SetDo(iso, cells[2], doa: "-", dob: "+");
        SetDo(iso, cells[3], doa: "-", dob: "+");

        iso.Db.SaveReaction("DOA-ID", panelId, "1", "0", "0", "3+", "3+");
        iso.Db.SaveReaction("DOA-ID", panelId, "2", "0", "0", "3+", "3+");
        iso.Db.SaveReaction("DOA-ID", panelId, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction("DOA-ID", panelId, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("DOA-ID", updateDb: false);
        Assert.True(result.Suspected.ContainsKey("anti-Doa"),
            $"Expected anti-Doa. Found: {string.Join(", ", result.Suspected.Keys)}");
        Assert.DoesNotContain("anti-Doa", result.RuledOut.Keys);
        Assert.Contains("anti-Dob", result.RuledOut.Keys);
    }

    [Fact]
    public void AnalyzeSpecimen_IgnoresDoaWhenNotOnAnyRunPanel()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("NO-DOA", "serum", null);
        var withDo = CreateDoPanel(iso, 2);
        var withoutDo = iso.Db.AddPanel("Plain", "L2", "V", 3, null, false);
        iso.Db.LinkSpecimenPanel("NO-DOA", withoutDo);

        var doCells = iso.Db.GetPanelCells(withDo);
        SetDo(iso, doCells[0], doa: "+", dob: "-");
        SetDo(iso, doCells[1], doa: "-", dob: "+");

        iso.Db.SaveReaction("NO-DOA", withoutDo, "1", "0", "0", "3+", "3+");
        iso.Db.SaveReaction("NO-DOA", withoutDo, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction("NO-DOA", withoutDo, "3", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("NO-DOA", updateDb: false);
        Assert.DoesNotContain("anti-Doa", result.Suspected.Keys);
        Assert.DoesNotContain("anti-Doa", result.RuledOut.Keys);
        Assert.DoesNotContain("anti-Dob", result.RuledOut.Keys);
    }

    [Fact]
    public void AnalyzeSpecimen_UnknownZygosityDoesNotRuleOutDoa()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("HET-DOA", "serum", null);
        var panelId = iso.Db.AddPanel("Doa only", "L", "V", 2, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Doa");
        iso.Db.LinkSpecimenPanel("HET-DOA", panelId);

        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Doa", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Doa", "-");
        iso.Db.SaveReaction("HET-DOA", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("HET-DOA", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("HET-DOA", updateDb: false);
        Assert.DoesNotContain("anti-Doa", result.RuledOut.Keys);
    }

    [Fact]
    public void AnalyzeSpecimen_HomozygousDoaNegativeRulesOut()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("RO-DOA", "serum", null);
        var panelId = CreateDoPanel(iso, 2);
        iso.Db.LinkSpecimenPanel("RO-DOA", panelId);

        var cells = iso.Db.GetPanelCells(panelId);
        SetDo(iso, cells[0], doa: "+", dob: "-");
        SetDo(iso, cells[1], doa: "-", dob: "+");
        iso.Db.SaveReaction("RO-DOA", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("RO-DOA", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("RO-DOA", updateDb: false);
        Assert.Contains("anti-Doa", result.RuledOut.Keys);
        Assert.Contains("anti-Dob", result.RuledOut.Keys);
    }

    [Fact]
    public void AnalyzeSpecimen_DttDoesNotRuleOutDoa_FicinDoes()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DTT-DOA", "serum", null);
        var dttPanel = CreateDoPanel(iso, 2);
        iso.Db.LinkSpecimenPanel("DTT-DOA", dttPanel);
        var dttCells = iso.Db.GetPanelCells(dttPanel);
        SetDo(iso, dttCells[0], doa: "+", dob: "-");
        SetDo(iso, dttCells[1], doa: "-", dob: "+");

        var dttRun = iso.Db.AddPanelRun("DTT-DOA", dttPanel, CellTreatment.DTT, SerumTreatment.None, "DTT");
        iso.Db.SaveReaction(dttRun, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(dttRun, "2", "0", "0", "0", "2+");

        var dttResult = iso.Analyzer.AnalyzeSpecimen("DTT-DOA", updateDb: false);
        Assert.DoesNotContain("anti-Doa", dttResult.RuledOut.Keys);
        Assert.Contains(dttResult.GatedRuleouts, g => g.Antibody == "anti-Doa");

        using var iso2 = new IsolatedDatabase();
        iso2.Db.AddSpecimen("FICIN-DOA", "serum", null);
        var ficinPanel = CreateDoPanel(iso2, 2);
        iso2.Db.LinkSpecimenPanel("FICIN-DOA", ficinPanel);
        var ficinCells = iso2.Db.GetPanelCells(ficinPanel);
        SetDo(iso2, ficinCells[0], doa: "+", dob: "-");
        SetDo(iso2, ficinCells[1], doa: "-", dob: "+");

        var ficinRun = iso2.Db.AddPanelRun("FICIN-DOA", ficinPanel, CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        iso2.Db.SaveReaction(ficinRun, "1", "0", "0", "0", "2+");
        iso2.Db.SaveReaction(ficinRun, "2", "0", "0", "0", "2+");

        var ficinResult = iso2.Analyzer.AnalyzeSpecimen("FICIN-DOA", updateDb: false);
        Assert.Contains("anti-Doa", ficinResult.RuledOut.Keys);
        Assert.DoesNotContain(ficinResult.GatedRuleouts, g => g.Antibody == "anti-Doa");
    }

    [Fact]
    public void AnalyzeSpecimen_DttGatesYtRuleout()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DTT-YT", "serum", null);
        var panelId = iso.Db.AddPanel("Yt", "L", "V", 2, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Yta");
        iso.Db.AddPanelExtraAntigen(panelId, "Ytb");
        iso.Db.LinkSpecimenPanel("DTT-YT", panelId);

        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Yta", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Ytb", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Yta", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Ytb", "+");

        var runId = iso.Db.AddPanelRun("DTT-YT", panelId, CellTreatment.DTT, SerumTreatment.None, "DTT");
        iso.Db.SaveReaction(runId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(runId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("DTT-YT", updateDb: false);
        Assert.DoesNotContain("anti-Yta", result.RuledOut.Keys);
        Assert.Contains(result.GatedRuleouts, g => g.Antibody == "anti-Yta");
    }

    [Fact]
    public void CopyPanelCells_CopiesWarehouseAntigens()
    {
        using var iso = new IsolatedDatabase();
        var source = CreateDoPanel(iso, 2);
        var cells = iso.Db.GetPanelCells(source);
        SetDo(iso, cells[0], doa: "+", dob: "-");

        var target = iso.Db.AddPanel("Copy", "L2", "V", 2, null, false);
        iso.Db.CopyPanelCells(source, target);

        Assert.Equal(new[] { "Doa", "Dob" }, iso.Db.GetPanelExtraAntigens(target));
        var copied = iso.Db.GetPanelCells(target).First(c => c.CellNumber == cells[0].CellNumber);
        Assert.Equal("+", copied.GetAntigen("Doa"));
        Assert.Equal("-", copied.GetAntigen("Dob"));
    }

    [Fact]
    public void RemovePanelExtraAntigen_DropsTyping()
    {
        using var iso = new IsolatedDatabase();
        var panelId = CreateDoPanel(iso, 1);
        iso.Db.RemovePanelExtraAntigen(panelId, "Doa");
        Assert.Equal(new[] { "Dob" }, iso.Db.GetPanelExtraAntigens(panelId));
        Assert.False(iso.Db.GetPanelCells(panelId).Single().HasTypedAntigen("Doa"));
        Assert.True(iso.Db.GetPanelCells(panelId).Single().HasTypedAntigen("Dob"));
    }

    [Fact]
    public void PanelCsv_RoundTrip_PreservesWarehouseAntigens()
    {
        using var iso = new IsolatedDatabase();
        var id = CreateDoPanel(iso, 2);
        var cells = iso.Db.GetPanelCells(id);
        SetDo(iso, cells[0], doa: "+", dob: "-");
        SetDo(iso, cells[1], doa: "-", dob: "+");

        var path = Path.Combine(Path.GetTempPath(), $"panel_wh_{Guid.NewGuid():N}.csv");
        try
        {
            PanelCsvService.Export(iso.Db.GetPanelCells(id), path);
            var imported = PanelCsvService.Import(path);
            Assert.True(imported.Success, string.Join("; ", imported.Errors));
            Assert.Equal("+", imported.Cells[0].Antigens["Doa"]);
            Assert.Equal("-", imported.Cells[0].Antigens["Dob"]);

            var newId = iso.Db.AddPanel("Imported Do", "L2", "V", 2, null, false);
            iso.Db.ReplacePanelCells(newId, imported.Cells.Select(c =>
            {
                var cell = new PanelCell { CellNumber = c.CellNumber };
                foreach (var (ag, val) in c.Antigens)
                    cell.SetAntigen(ag, val);
                return cell;
            }).ToList());

            Assert.Equal(new[] { "Doa", "Dob" }, iso.Db.GetPanelExtraAntigens(newId));
            var round = iso.Db.GetPanelCells(newId).First(c => c.CellNumber == "1");
            Assert.Equal("+", round.GetAntigen("Doa"));
            Assert.Equal("-", round.GetAntigen("Dob"));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Search_FindsCellsByWarehouseAntigen()
    {
        using var iso = new IsolatedDatabase();
        var panelId = CreateDoPanel(iso, 2);
        var cells = iso.Db.GetPanelCells(panelId);
        SetDo(iso, cells[0], doa: "+", dob: "-");
        SetDo(iso, cells[1], doa: "-", dob: "+");

        var matches = iso.Db.SearchCellsByProfile(new Dictionary<string, string> { ["Doa"] = "+" });
        Assert.Single(matches);
        Assert.Equal("+", matches[0].cell.GetAntigen("Doa"));
    }

    [Fact]
    public void GetAnalyzedAntigens_IncludesOnlyTypedExtras()
    {
        var none = AntigenConstants.GetAnalyzedAntigens(Array.Empty<string>());
        Assert.Equal(AntigenConstants.Antigens, none);

        var withDo = AntigenConstants.GetAnalyzedAntigens(new[] { "Doa", "Vel" });
        Assert.Contains("Doa", withDo);
        Assert.Contains("Vel", withDo);
        Assert.DoesNotContain("Dob", withDo);
        Assert.Equal(AntigenConstants.Antigens.Count + 2, withDo.Count);
    }

    private static int CreateDoPanel(IsolatedDatabase iso, int cells)
    {
        var id = iso.Db.AddPanel("Do panel", "LOT-DO", "Vendor", cells, null, false);
        iso.Db.AddPanelExtraAntigen(id, "Doa");
        iso.Db.AddPanelExtraAntigen(id, "Dob");
        return id;
    }

    private static void SetDo(IsolatedDatabase iso, PanelCell cell, string doa, string dob)
    {
        iso.Db.UpdatePanelCellAntigen(cell.Id, "Doa", doa);
        iso.Db.UpdatePanelCellAntigen(cell.Id, "Dob", dob);
    }
}
