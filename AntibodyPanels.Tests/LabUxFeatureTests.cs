using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class LabUxFeatureTests
{
    [Fact]
    public void AddSpecimen_StoresClinicalContext()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("CLIN-001", "serum", "2030-01-01", true,
            "Warm auto workup", "R1r, K-", "anti-E", "1+");
        var s = iso.Db.GetSpecimen("CLIN-001");
        Assert.Equal("Warm auto workup", s!.Notes);
        Assert.Equal("R1r, K-", s.Phenotype);
        Assert.Equal("anti-E", s.PreviousAntibodies);
        Assert.Equal("1+", s.DatResult);
    }

    [Fact]
    public void UpdateSpecimen_ReplacesClinicalContext()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("CLIN-002", "plasma", null);
        iso.Db.UpdateSpecimen("CLIN-002", "plasma", null, true, "note", "rr", "anti-K", "Negative");
        var s = iso.Db.GetSpecimen("CLIN-002")!;
        Assert.Equal("note", s.Notes);
        Assert.Equal("rr", s.Phenotype);
        Assert.Equal("anti-K", s.PreviousAntibodies);
        Assert.Equal("Negative", s.DatResult);
    }

    [Fact]
    public void PanelCsv_RoundTrip_PreservesAntigens()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("CSV Panel", "LOT1", "Vendor", 2, null, true, 1);
        var cells = iso.Db.GetPanelCells(id);
        cells[0].SetAntigen("D", "+");
        cells[0].SetAntigen("C", "-");
        cells[0].SetAntigen("K", "+");
        iso.Db.UpdatePanelCell(cells[0]);

        var path = Path.Combine(Path.GetTempPath(), $"panel_{Guid.NewGuid():N}.csv");
        try
        {
            PanelCsvService.Export(iso.Db.GetPanelCells(id), path);
            var imported = PanelCsvService.Import(path);
            Assert.True(imported.Success, string.Join("; ", imported.Errors));
            Assert.Contains(imported.Cells, c => c.CellNumber == "1" && c.Antigens["D"] == "+" && c.Antigens["K"] == "+");

            var newId = iso.Db.AddPanel("Imported", "L2", "V", 1, null, false);
            iso.Db.ReplacePanelCells(newId, imported.Cells.Select(c =>
            {
                var cell = new PanelCell { CellNumber = c.CellNumber };
                foreach (var ag in AntigenConstants.Antigens)
                    cell.SetAntigen(ag, c.Antigens.TryGetValue(ag, out var v) ? v : "-");
                return cell;
            }).ToList());
            var round = iso.Db.GetPanelCells(newId);
            Assert.Equal("+", round.First(c => c.CellNumber == "1").GetAntigen("D"));
            Assert.Equal("+", round.First(c => c.CellNumber == "1").GetAntigen("K"));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Worklist_IncludesExpiringSpecimen()
    {
        using var iso = new IsolatedDatabase();
        var soon = DateTime.Now.AddDays(5).ToString("yyyy-MM-dd");
        iso.Db.AddSpecimen("EXP-SOON", "serum", soon);
        var items = iso.Db.GetWorklistItems(14);
        Assert.Contains(items, i => i.AccessionNumber == "EXP-SOON" && i.Kind == WorklistKind.ExpiringSpecimen);
    }

    [Fact]
    public void Worklist_ConfirmedId_DropsIncompleteAndStaleItems()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DONE-001", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("DONE-001", panelId);

        var before = iso.Db.GetWorklistItems(14)
            .Where(i => i.AccessionNumber == "DONE-001")
            .ToList();
        Assert.Contains(before, i => i.Kind == WorklistKind.IncompleteReactions);
        Assert.Contains(before, i => i.Kind == WorklistKind.StaleAnalysis);

        iso.Db.SetSpecimenFinalCall("DONE-001", "anti-E", null, "DP");
        var after = iso.Db.GetWorklistItems(14)
            .Where(i => i.AccessionNumber == "DONE-001")
            .ToList();
        Assert.DoesNotContain(after, i => i.Kind == WorklistKind.IncompleteReactions);
        Assert.DoesNotContain(after, i => i.Kind == WorklistKind.StaleAnalysis);

        iso.Db.ClearSpecimenFinalCall("DONE-001");
        var restored = iso.Db.GetWorklistItems(14)
            .Where(i => i.AccessionNumber == "DONE-001")
            .ToList();
        Assert.Contains(restored, i => i.Kind == WorklistKind.IncompleteReactions);
        Assert.Contains(restored, i => i.Kind == WorklistKind.StaleAnalysis);
    }

    [Fact]
    public void Worklist_ConfirmedId_StillShowsExpiringSpecimen()
    {
        using var iso = new IsolatedDatabase();
        var soon = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd");
        iso.Db.AddSpecimen("DONE-EXP", "serum", soon);
        iso.Db.SetSpecimenFinalCall("DONE-EXP", "anti-K", null, "DP");

        var items = iso.Db.GetWorklistItems(14)
            .Where(i => i.AccessionNumber == "DONE-EXP")
            .ToList();
        Assert.DoesNotContain(items, i => i.Kind == WorklistKind.IncompleteReactions);
        Assert.DoesNotContain(items, i => i.Kind == WorklistKind.StaleAnalysis);
        Assert.Contains(items, i => i.Kind == WorklistKind.ExpiringSpecimen);
    }

    [Fact]
    public void ClinicalIdentificationReport_ContainsWorksheetAndSignOff()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("RPT-001", "serum", null, true, "history note", "R1R1", null, "Negative");
        var panelId = iso.Db.AddPanel("ID Panel", "LOT-X", "Ortho", 1, null, false);
        iso.Db.LinkSpecimenPanel("RPT-001", panelId);
        iso.Db.SaveReaction("RPT-001", panelId, "1", "0", "0", "2+", "2+");

        var text = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "RPT-001");
        Assert.Contains("ANTIBODY IDENTIFICATION WORKSHEET", text);
        Assert.Contains("RPT-001", text);
        Assert.Contains("history note", text);
        Assert.Contains("R1R1", text);
        Assert.Contains("Technologist:", text);
        Assert.Contains("Supervisor:", text);
        Assert.Contains(AppSettings.Current.LabName.ToUpperInvariant(), text);
    }

    [Theory]
    [InlineData("+", "+")]
    [InlineData("-", "-")]
    [InlineData("pos", "+")]
    [InlineData("", "-")]
    public void PanelCsv_NormalizesAntigenValues(string raw, string expected)
    {
        Assert.Equal(expected, PanelCsvService.NormalizeAntigen(raw));
    }

    [Fact]
    public void LabSettings_Clamp_KeepsThresholdInRange()
    {
        var s = new LabSettings { ProbabilityThreshold = 1.5, ExpirationWarningDays = 0, LabName = " " };
        s.Clamp();
        Assert.Equal(0.95, s.ProbabilityThreshold);
        Assert.Equal(1, s.ExpirationWarningDays);
        Assert.Equal("Immunohematology Laboratory", s.LabName);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 3)]
    [InlineData(-1, 3)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void LabSettings_Clamp_KeepsIdentificationCellCountInRange(int input, int expected)
    {
        var s = new LabSettings { IdentificationCellCount = input };
        s.Clamp();
        Assert.Equal(expected, s.IdentificationCellCount);
        Assert.Equal($"{expected} + {expected}", s.IdentificationRuleLabel);
    }

    [Fact]
    public void CopyReactions_CopiesGradesToNewRun()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("COPY-001", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("COPY-001", panelId);
        iso.Db.SaveReaction("COPY-001", panelId, "1", "0", "0", "3+", "3+");
        iso.Db.SaveReaction("COPY-001", panelId, "2", "0", "1+", "2+", "2+");
        var source = iso.Db.GetPanelRuns("COPY-001", panelId).Single();
        var destId = iso.Db.AddPanelRun("COPY-001", panelId, CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        var copied = iso.Db.CopyReactions(source.RunId, destId);
        Assert.Equal(2, copied);
        var dest = iso.Db.GetReactions(destId).ToDictionary(r => r.CellNumber);
        Assert.Equal("3+", dest["1"].AHG);
        Assert.Equal("2+", dest["2"].AHG);
        Assert.Equal("1+", dest["2"].C37);
    }

    [Fact]
    public void PanelAntigram_ContainsGridNotPanelSummaryHeading()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("Bench Panel", "LOT-A", "Ortho", 2, null, false);
        var text = iso.Reports.GeneratePreviewText(ReportType.PanelAntigram, panelId: id);
        Assert.Contains("PANEL ANTIGRAM", text);
        Assert.DoesNotContain("PANEL SUMMARY", text);
        Assert.Contains("Bench Panel", text);
        Assert.Contains("LOT-A", text);
        foreach (var cell in iso.Db.GetPanelCells(id))
            Assert.Contains(cell.CellNumber, text);
    }

    [Fact]
    public void FinalCall_PersistsAndAppearsOnClinicalWorksheet()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("FINAL-001", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("FINAL-001", panelId);
        iso.Db.SaveReaction("FINAL-001", panelId, "1", "0", "0", "2+", "2+");
        iso.Db.SetSpecimenFinalCall("FINAL-001", "anti-E", "dosage noted", "DP");
        var stored = iso.Db.GetSpecimen("FINAL-001")!;
        Assert.True(stored.HasFinalCall);
        Assert.Equal("anti-E", stored.FinalAntibodies);
        Assert.Equal("DP", stored.IdentifiedBy);

        var text = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "FINAL-001");
        Assert.Contains("FINAL IDENTIFICATION (confirmed)", text);
        Assert.Contains("anti-E", text);
        Assert.Contains("Confirmed by DP", text);
        Assert.Contains("dosage noted", text);
    }

    [Fact]
    public void SearchCellsByProfile_StillFiltersByAntigenCriteria()
    {
        using var iso = new IsolatedDatabase();
        var id = iso.Db.AddPanel("SearchP", "L", "V", 2, null, false);
        var cells = iso.Db.GetPanelCells(id);
        cells[0].SetAntigen("K", "+");
        cells[0].SetAntigen("D", "-");
        cells[1].SetAntigen("K", "-");
        cells[1].SetAntigen("D", "+");
        iso.Db.UpdatePanelCell(cells[0]);
        iso.Db.UpdatePanelCell(cells[1]);

        var matches = iso.Db.SearchCellsByProfile(new Dictionary<string, string> { ["K"] = "+" });
        Assert.Single(matches);
        Assert.Equal(cells[0].CellNumber, matches[0].cell.CellNumber);
    }
}
