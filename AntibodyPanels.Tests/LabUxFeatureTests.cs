using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;
using AntibodyPanels.ViewModels;

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
    public void WorklistItem_MatchesFilter_SearchesTitleDetailAndAccession()
    {
        var item = new WorklistItem
        {
            KindLabel = "Incomplete",
            Title = "2024-ABC",
            Detail = "4 of 16 cells missing grades",
            UrgencyLabel = "Today",
            AccessionNumber = "2024-ABC"
        };
        Assert.True(item.MatchesFilter("abc"));
        Assert.True(item.MatchesFilter("incomplete"));
        Assert.True(item.MatchesFilter("missing"));
        Assert.True(item.MatchesFilter("today"));
        Assert.False(item.MatchesFilter("xyz"));
        Assert.True(item.MatchesFilter(""));
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
    public void Worklist_IncludesExpiredSpecimen()
    {
        using var iso = new IsolatedDatabase();
        var expired = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
        iso.Db.AddSpecimen("EXP-PAST", "serum", expired, true);
        var items = iso.Db.GetWorklistItems(14);
        var row = Assert.Single(items, i => i.AccessionNumber == "EXP-PAST" && i.Kind == WorklistKind.ExpiredSpecimen);
        Assert.Equal("Expired", row.UrgencyLabel);
        Assert.Equal(0, row.SortOrder);
        Assert.Contains("Expired", row.Detail);
    }

    [Fact]
    public void Worklist_IncludesExpiredPanel()
    {
        using var iso = new IsolatedDatabase();
        var expired = DateTime.Now.AddDays(-5).ToString("yyyy-MM-dd");
        iso.Db.AddPanel("Old Lot", "LOT-Z", "Vendor", 1, expired, true, 1, true);
        var items = iso.Db.GetWorklistItems(14);
        Assert.Contains(items, i => i.Kind == WorklistKind.ExpiredPanel && i.Title == "Old Lot");
    }

    [Fact]
    public void Worklist_FarFutureExpiration_NotListed()
    {
        using var iso = new IsolatedDatabase();
        var far = DateTime.Now.AddDays(60).ToString("yyyy-MM-dd");
        iso.Db.AddSpecimen("EXP-FAR", "serum", far);
        var items = iso.Db.GetWorklistItems(14);
        Assert.DoesNotContain(items, i => i.AccessionNumber == "EXP-FAR" &&
            (i.Kind == WorklistKind.ExpiringSpecimen || i.Kind == WorklistKind.ExpiredSpecimen));
    }

    [Fact]
    public void Worklist_ConfirmedId_StillShowsExpiredSpecimen()
    {
        using var iso = new IsolatedDatabase();
        var expired = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        iso.Db.AddSpecimen("DONE-PAST", "serum", expired, true);
        iso.Db.SetSpecimenFinalCall("DONE-PAST", "anti-K", null, "DP");

        var items = iso.Db.GetWorklistItems(14)
            .Where(i => i.AccessionNumber == "DONE-PAST")
            .ToList();
        Assert.DoesNotContain(items, i => i.Kind == WorklistKind.IncompleteReactions);
        Assert.Contains(items, i => i.Kind == WorklistKind.ExpiredSpecimen);
    }

    [Fact]
    public void LabSettings_WorklistFilters_HideUncheckedKinds()
    {
        var s = new LabSettings
        {
            WorklistShowIncomplete = false,
            WorklistShowStale = true,
            WorklistShowExpiring = false,
            WorklistShowExpired = true
        };
        Assert.False(s.ShowsWorklistKind(WorklistKind.IncompleteReactions));
        Assert.True(s.ShowsWorklistKind(WorklistKind.StaleAnalysis));
        Assert.False(s.ShowsWorklistKind(WorklistKind.ExpiringSpecimen));
        Assert.False(s.ShowsWorklistKind(WorklistKind.ExpiringPanel));
        Assert.True(s.ShowsWorklistKind(WorklistKind.ExpiredSpecimen));
        Assert.True(s.ShowsWorklistKind(WorklistKind.ExpiredPanel));
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
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(14, 14)]
    [InlineData(20, 14)]
    public void LabSettings_Clamp_KeepsSpecimenDatingDaysInRange(int input, int expected)
    {
        var s = new LabSettings { DefaultSpecimenDatingDays = input };
        s.Clamp();
        Assert.Equal(expected, s.DefaultSpecimenDatingDays);
    }

    [Fact]
    public void DefaultExpirationDate_AddsDatingDays()
    {
        var today = new DateTime(2026, 9, 3);
        Assert.Equal(new DateTime(2026, 9, 6), LabSettings.DefaultExpirationDate(today, 3));
        Assert.Null(LabSettings.DefaultExpirationDate(today, 0));
        Assert.Null(LabSettings.DefaultExpirationDate(today, -2));
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

    [Theory]
    [InlineData(0, 3)]
    [InlineData(6, 3)]
    [InlineData(-1, 3)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void LabSettings_Clamp_KeepsAcsRuleoutCountInRange(int input, int expected)
    {
        var s = new LabSettings { AcsRuleoutCount = input };
        s.Clamp();
        Assert.Equal(expected, s.AcsRuleoutCount);
    }

    [Fact]
    public void LabSettings_Default_AcsRuleoutCountIsThree()
    {
        Assert.Equal(3, LabSettings.CreateDefault().AcsRuleoutCount);
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

    [Theory]
    [InlineData("", "anti-E", "anti-E")]
    [InlineData("anti-E", "anti-K", "anti-E; anti-K")]
    [InlineData("anti-E", "anti-E", "anti-E")]
    [InlineData("anti-E; anti-K", "anti-E", "anti-E; anti-K")]
    [InlineData("  anti-E  ", "anti-K", "anti-E; anti-K")]
    [InlineData("anti-E, anti-c", "anti-K", "anti-E; anti-c; anti-K")]
    [InlineData(null, "anti-E", "anti-E")]
    [InlineData("anti-E", "", "anti-E")]
    public void AppendAntibodyToFinalId_MergesWithoutDuplicates(string? current, string add, string expected)
    {
        Assert.Equal(expected, AnalysisViewModel.AppendAntibodyToFinalId(current, add));
    }

    [Fact]
    public void SuggestedFinalId_IncludesOnlyAntibodiesThatMeetRule()
    {
        var rows = new[]
        {
            new SuspectedRow { Antibody = "anti-E", MeetsIdentificationRule = true },
            new SuspectedRow { Antibody = "anti-K", MeetsIdentificationRule = false },
            new SuspectedRow { Antibody = "anti-c", MeetsIdentificationRule = true }
        };
        Assert.Equal("anti-E; anti-c", AnalysisViewModel.SuggestedFinalId(rows));
    }

    [Fact]
    public void SuggestedFinalId_EmptyWhenNoneMeetRule()
    {
        var rows = new[]
        {
            new SuspectedRow { Antibody = "anti-E", MeetsIdentificationRule = false }
        };
        Assert.Equal("", AnalysisViewModel.SuggestedFinalId(rows));
    }

    [Fact]
    public void SuggestedFinalId_PrefersAcsWhenEligible()
    {
        var rows = new[]
        {
            new SuspectedRow { Antibody = "anti-E", MeetsIdentificationRule = true }
        };
        var acs = new AcsEvaluation { IsEligible = true };
        Assert.Equal(AntigenConstants.AcsResultText, AnalysisViewModel.SuggestedFinalId(rows, acs));
    }

    [Fact]
    public void SuggestedFinalId_DoesNotPrefillAcsOnException()
    {
        var rows = new[]
        {
            new SuspectedRow { Antibody = "anti-E", MeetsIdentificationRule = true }
        };
        var acs = new AcsEvaluation
        {
            IsEligible = false,
            IsEligibleWithException = true,
            Exceptions = { new AcsExceptionAntibody { Antibody = "anti-E", CombinedScore = 0.97, RuleoutCount = 3 } }
        };
        Assert.Equal("anti-E", AnalysisViewModel.SuggestedFinalId(rows, acs));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("anti-E", "anti-E")]
    public void Specimen_FinalIdDisplay_ShowsConfirmedIdOnly(string? finalId, string expected)
    {
        var s = new Specimen { FinalAntibodies = finalId };
        Assert.Equal(expected, s.FinalIdDisplay);
        Assert.Equal(!string.IsNullOrWhiteSpace(finalId), s.HasFinalCall);
    }

    [Fact]
    public void Reports_SelectSpecimen_OpensClinicalIdentification()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("NAV-001", "serum", null);
        iso.Db.AddSpecimen("NAV-002", "plasma", null);
        var vm = new ReportsViewModel(iso.Db);
        vm.SelectSpecimen("NAV-002", "Clinical Identification");
        Assert.Equal("NAV-002", vm.SelectedSpecimen?.AccessionNumber);
        Assert.Equal("Clinical Identification", vm.SelectedReportType);
        Assert.Contains("NAV-002", vm.PreviewText);
        Assert.Contains("ANTIBODY IDENTIFICATION WORKSHEET", vm.PreviewText);
    }

    [Fact]
    public void Reports_SelectSpecimen_ClearsFilterThatHidesTarget()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("HIDE-001", "serum", null);
        var vm = new ReportsViewModel(iso.Db);
        vm.SpecimenFilter = "plasma";
        Assert.DoesNotContain(vm.Specimens, s => s.AccessionNumber == "HIDE-001");
        vm.SelectSpecimen("HIDE-001", "Specimen Summary");
        Assert.Equal("", vm.SpecimenFilter);
        Assert.Equal("HIDE-001", vm.SelectedSpecimen?.AccessionNumber);
        Assert.Equal("Specimen Summary", vm.SelectedReportType);
    }

    [Fact]
    public void AllSpecimensReport_IncludesStatusAndFinalIdColumns()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("STATUS-001", "serum", null);
        iso.Db.AddSpecimen("STATUS-002", "plasma", null);
        iso.Db.SetSpecimenFinalCall("STATUS-002", "anti-E", null, "DP");
        var text = iso.Reports.GeneratePreviewText(ReportType.AllSpecimens);
        Assert.Contains("Status", text);
        Assert.Contains("Final ID", text);
        Assert.Contains("Not analyzed", text);  // STATUS-001
        Assert.Contains("Confirmed", text);      // STATUS-002
        Assert.Contains("anti-E", text);
    }

    [Fact]
    public void PendingWorkReport_SeparatesSections()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PW-001", "serum", null);
        iso.Db.AddSpecimen("PW-002", "plasma", null);
        iso.Db.SetSpecimenFinalCall("PW-002", "anti-K", null, "DP");
        var text = iso.Reports.GeneratePreviewText(ReportType.PendingWork);
        Assert.Contains("PENDING WORK SUMMARY", text);
        Assert.Contains("NOT YET ANALYZED", text);
        Assert.Contains("PW-001", text);
        Assert.DoesNotContain("PW-002", text); // confirmed, not pending
        Assert.Contains("Confirmed:         1", text);
        Assert.Contains("Pending:           1", text);
    }

    [Fact]
    public void PendingWorkReport_AllConfirmed_ShowsNoPendingMessage()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PW-ALL", "serum", null);
        iso.Db.SetSpecimenFinalCall("PW-ALL", "anti-D", null, "DP");
        var text = iso.Reports.GeneratePreviewText(ReportType.PendingWork);
        Assert.Contains("No pending work", text);
    }

    [Fact]
    public void Specimen_MatchesFilter_SearchesClinicalFields()
    {
        var s = new Specimen
        {
            AccessionNumber = "2024-ABC",
            Type = "plasma",
            Phenotype = "R1r, K-",
            PreviousAntibodies = "anti-E",
            Notes = "Warm auto workup",
            DatResult = "1+",
            FinalAntibodies = "anti-c"
        };
        Assert.True(s.MatchesFilter("abc"));
        Assert.True(s.MatchesFilter("plasma"));
        Assert.True(s.MatchesFilter("R1r"));
        Assert.True(s.MatchesFilter("anti-E"));
        Assert.True(s.MatchesFilter("warm"));
        Assert.True(s.MatchesFilter("anti-c"));
        Assert.False(s.MatchesFilter("xyz"));
        Assert.True(s.MatchesFilter(""));
        Assert.True(s.MatchesFilter(null));
    }

    [Fact]
    public void Panel_MatchesFilter_SearchesNameLotAndVendor()
    {
        var p = new Panel { Name = "Ortho ID Panel", LotNumber = "LOT-99", Vendor = "Ortho", ExpirationDate = "2030-01-01" };
        Assert.True(p.MatchesFilter("ortho"));
        Assert.True(p.MatchesFilter("99"));
        Assert.True(p.MatchesFilter("2030"));
        Assert.False(p.MatchesFilter("Immucor"));
    }

    [Theory]
    [InlineData("SPECIMEN SUMMARY — 2024-001", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Error generating report: boom", false)]
    public void ReportPreview_CanShare_RejectsEmptyAndErrors(string text, bool expected)
    {
        Assert.Equal(expected, ReportsViewModel.CanSharePreview(text));
    }

    [Fact]
    public void ReportPrintLines_IncludesTitleAndBody()
    {
        var lines = ReportsViewModel.PrintLines("Clinical Identification", "Line A\nLine B\n");
        Assert.Equal("Clinical Identification", lines[0]);
        Assert.Equal("Line A", lines[1]);
        Assert.Equal("Line B", lines[2]);
        Assert.Equal("", lines[3]);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1+", true)]
    [InlineData("4+", true)]
    [InlineData("NT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReactionRow_IsGradeEntered(string? value, bool expected)
    {
        Assert.Equal(expected, ReactionRow.IsGradeEntered(value));
    }

    [Theory]
    [InlineData(0, 16, "Grades entered: 0 of 16 cells.")]
    [InlineData(8, 16, "Grades entered: 8 of 16 cells.")]
    [InlineData(16, 16, "All 16 cells have grades.")]
    [InlineData(0, 0, "")]
    public void ReactionEntryProgress_FormatsCounts(int entered, int total, string expected)
    {
        Assert.Equal(expected, ReactionsViewModel.FormatEntryProgress(entered, total));
    }
}
