using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ZygosityPersistenceTests
{
    [Theory]
    [InlineData("++", "++")]
    [InlineData("+/+", "++")]
    [InlineData("HOMO", "++")]
    [InlineData("homozygous", "++")]
    [InlineData("+", "+")]
    public void VendorImport_KeepsExplicitHomozygousMark(string raw, string expected)
    {
        Assert.Equal(expected, VendorAntigenAliases.NormalizeValue(raw, required: true));
        Assert.Equal(expected, PanelCsvService.NormalizeAntigen(raw));
    }

    [Fact]
    public void WeakPlus_StaysPositive_NotHomozygous()
    {
        Assert.Equal("+", VendorAntigenAliases.NormalizeValue("+w", required: true));
        Assert.Null(PanelCsvService.NormalizeAntigen("+w"));
    }

    [Fact]
    public void ExplicitDoublePlus_IsPositive_ButPartnerIsNotInvented()
    {
        var cell = Cell(("E", "++"));
        Assert.True(cell.HasTypedAntigen("E"));
        Assert.True(cell.IsAntigenPositive("E"));
        Assert.True(cell.IsExplicitlyHomozygous("E"));
        Assert.Equal("+", cell.GetAntigen("E"));
        Assert.Equal("++", cell.GetTypedValue("E"));
        Assert.False(cell.HasTypedAntigen("e"));
        Assert.True(cell.IsHomozygousFor("E"));
        Assert.Equal("-", cell.GetAntigen("e"));
    }

    [Fact]
    public void SinglePlus_WithUntypedPartner_IsNotHomozygous()
    {
        var cell = Cell(("E", "+"));
        Assert.False(cell.IsHomozygousFor("E"));
        Assert.Equal(AntigenZygosity.PositiveUnknown, AntigramDisplay.Classify(cell, "E"));
        Assert.Equal("+/?", AntigramDisplay.Format(cell.Antigens, "E", showDosage: true));
        Assert.Contains("e was not typed, so zygosity is unknown", AntigramDisplay.Explain("3", cell.Antigens, "E"));
    }

    [Fact]
    public void ExplicitDoublePlus_DisplaysHomozygous_WithoutInventingPartnerNegative()
    {
        var antigens = new Dictionary<string, string> { ["E"] = "++" };
        Assert.Equal(AntigenZygosity.Homozygous, AntigramDisplay.Classify(antigens, "E"));
        Assert.Equal("++", AntigramDisplay.Format(antigens, "E", showDosage: true));
        Assert.Equal("+", AntigramDisplay.Format(antigens, "E", showDosage: false));
        var sentence = AntigramDisplay.Explain("1", antigens, "E");
        Assert.Contains("homozygous for E", sentence);
        Assert.Contains("e was not typed", sentence);
        Assert.DoesNotContain("e−", sentence);
        Assert.False(antigens.ContainsKey("e"));
    }

    [Fact]
    public void ExplicitDoublePlus_WithTypedPartnerPositive_IsConflictNotHomozygous()
    {
        var cell = Cell(("E", "++"), ("e", "+"));
        Assert.False(cell.IsHomozygousFor("E"));
        Assert.Equal(AntigenZygosity.Heterozygous, AntigramDisplay.Classify(cell, "E"));
        Assert.Contains("heterozygous for E (E+ e+", AntigramDisplay.Explain("4", cell.Antigens, "E"));
    }

    [Fact]
    public void VendorCsv_PersistsDoublePlus_AndRulesOutOnNonreactiveCell()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,++,NT\n",
            "BIO-E-PLUSPLUS");
        var cell = iso.Db.GetPanelCells(panelId).Single(c => c.CellNumber == "1");
        Assert.Equal("++", cell.GetTypedValue("E"));
        Assert.False(cell.HasTypedAntigen("e"));
        Assert.True(cell.IsHomozygousFor("E"));

        iso.Db.AddSpecimen("ZYG-E-PP", "serum", null);
        iso.Db.LinkSpecimenPanel("ZYG-E-PP", panelId);
        iso.Db.SaveReaction("ZYG-E-PP", panelId, "1", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ZYG-E-PP", updateDb: false);
        var ev = result.RuleoutEvaluations.Single(e => e.Antibody == "anti-E");
        Assert.True(ev.MeetsCriteria);
        Assert.Equal(1, ev.ObservedCount);
        Assert.Contains(ev.Cells, c => c.CellNumber == "1" && c.IsHomozygous);
        Assert.True(result.RuledOut.ContainsKey("anti-E"));
        Assert.DoesNotContain(result.Suggestions, s => s.Contains("auto-confirm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VendorCsv_SinglePlusUntypedPartner_StillDoesNotRuleOut()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,+,NT\n",
            "BIO-E-PLUS-NT");
        var cell = iso.Db.GetPanelCells(panelId).Single(c => c.CellNumber == "1");
        Assert.Equal("+", cell.GetTypedValue("E"));
        Assert.False(cell.IsHomozygousFor("E"));

        iso.Db.AddSpecimen("ZYG-E-NT", "serum", null);
        iso.Db.LinkSpecimenPanel("ZYG-E-NT", panelId);
        iso.Db.SaveReaction("ZYG-E-NT", panelId, "1", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ZYG-E-NT", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.DoesNotContain(result.RuleoutEvaluations, e => e.Antibody == "anti-E" && e.MeetsCriteria);
    }

    [Fact]
    public void ReactiveDoublePlusCell_IsConflict_NotARuleout()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,++,NT\n" +
            "2,-,+,+,-,+\n",
            "BIO-E-PP-RXN");
        iso.Db.AddSpecimen("ZYG-E-RXN", "serum", null);
        iso.Db.LinkSpecimenPanel("ZYG-E-RXN", panelId);
        iso.Db.SaveReaction("ZYG-E-RXN", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("ZYG-E-RXN", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ZYG-E-RXN", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        var ev = result.RuleoutEvaluations.SingleOrDefault(e => e.Antibody == "anti-E");
        Assert.True(ev == null || ev.ObservedCount == 0);
    }

    [Fact]
    public void SelectedCell_ExplicitDoublePlus_OutranksSinglePlusWithUntypedPartner()
    {
        var stock = new Panel { PanelId = 9, Name = "Select", LotNumber = "SEL" };
        var inventory = new (Panel, PanelCell)[]
        {
            (stock, Named("1", ("E", "+"))),
            (stock, Named("2", ("E", "++"))),
        };
        var result = new AnalysisResult
        {
            Suspected = { ["anti-E"] = 0.8 },
            SuspectedStatistics =
            {
                ["anti-E"] = new SuspectedStatistics
                {
                    PositiveAgPositiveCount = 1,
                    NegativeAgNegativeCount = 1,
                    IdentificationRequired = 3,
                    MeetsIdentificationRule = false
                }
            }
        };

        var recs = SelectedCellRecommender.Recommend(result, Array.Empty<(int, string)>(), inventory);
        Assert.NotEmpty(recs);
        Assert.Equal("2", recs[0].CellNumber);
        Assert.Contains("homozygous", recs[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_FindsExplicitHomozygous_WhenPartnerIsUntyped()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("C ++ panel", "L", "V", 2, null, false);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "C", "++");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", AntigenConstants.AntigenNotTested);
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "C", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", AntigenConstants.AntigenNotTested);

        var both = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityBoth);
        var homo = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityHomozygous);
        var het = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["C"] = "+" },
            AntigenConstants.ZygosityHeterozygous);

        Assert.Equal(2, both.Count);
        Assert.Single(homo);
        Assert.Equal(cells[0].CellNumber, homo[0].cell.CellNumber);
        Assert.Equal("++", homo[0].cell.GetTypedValue("C"));
        Assert.False(homo[0].cell.HasTypedAntigen("c"));
        Assert.Empty(het);
    }

    [Fact]
    public void Warehouse_ExplicitDoublePlus_IsHomozygousWithoutPartner()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Do ++", "L", "V", 1, null, false);
        iso.Db.AddPanelExtraAntigen(panelId, "Doa");
        var cell = iso.Db.GetPanelCells(panelId).Single();
        iso.Db.UpdatePanelCellAntigen(cell.Id, "Doa", "++");
        iso.Db.UpdatePanelCell(iso.Db.GetPanelCells(panelId).Single());

        var reloaded = iso.Db.GetPanelCells(panelId).Single();
        Assert.Equal("++", reloaded.GetTypedValue("Doa"));
        Assert.True(reloaded.IsHomozygousFor("Doa"));

        var homo = iso.Db.SearchCellsByProfile(
            new Dictionary<string, string> { ["Doa"] = "+" },
            AntigenConstants.ZygosityHomozygous);
        Assert.Single(homo);
        Assert.Equal("++", homo[0].cell.GetTypedValue("Doa"));
    }

    [Fact]
    public void JsonImport_PersistsDoublePlus()
    {
        const string json = """
            {
              "vendor": "Immucor",
              "name": "Zygosity structured",
              "lotNumber": "JSON-ZYG-1",
              "cells": [
                { "cellNumber": "1", "antigens": { "E": "++", "e": "NT" } }
              ]
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"zyg_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.Immucor, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Equal("++", parsed.Cells[0].GetTypedValue("E"));
            Assert.False(parsed.Cells[0].HasTypedAntigen("e"));
            Assert.True(parsed.Cells[0].IsHomozygousFor("E"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static PanelCell Cell(params (string Ag, string Val)[] antigens) =>
        Named("1", antigens);

    private static PanelCell Named(string number, params (string Ag, string Val)[] antigens)
    {
        var cell = new PanelCell { CellNumber = number };
        foreach (var (ag, val) in antigens)
            cell.SetAntigen(ag, val);
        return cell;
    }

    private static int PersistSubset(IsolatedDatabase iso, string csv, string lot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_zyg_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, csv);
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = lot;
            parsed.SourceFormat = "csv";
            return new VendorPanelImportService(iso.Db).Persist(parsed);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
