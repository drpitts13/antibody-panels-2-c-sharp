using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

public class AntibodyAnalyticsTests
{
    [Fact]
    public void MultiSpecificCall_IncrementsBothSpecificities()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("AN-001", "serum");
        iso.Db.SetSpecimenFinalCall("AN-001", "anti-E; anti-K", null, "DP",
            new DateTime(2026, 3, 15, 10, 0, 0));

        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31));

        Assert.Equal(1, result.ConfirmedSpecimens);
        Assert.Equal(2, result.DistinctSpecificities);
        Assert.Equal(2, result.TotalOccurrences);
        Assert.Equal(1, result.Rows.Single(r => r.Antibody == "anti-E").Count);
        Assert.Equal(1, result.Rows.Single(r => r.Antibody == "anti-K").Count);
        Assert.Equal(1, result.Rows.Single(r => r.Antibody == "anti-E").Specimens);
    }

    [Fact]
    public void CaseInsensitiveGrouping_UsesMostCommonCasing()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("AN-E1", "serum");
        iso.Db.AddSpecimen("AN-E2", "serum");
        iso.Db.AddSpecimen("AN-E3", "serum");
        iso.Db.SetSpecimenFinalCall("AN-E1", "anti-E", null, "DP", new DateTime(2026, 2, 1));
        iso.Db.SetSpecimenFinalCall("AN-E2", "Anti-E", null, "DP", new DateTime(2026, 2, 2));
        iso.Db.SetSpecimenFinalCall("AN-E3", "anti-E", null, "DP", new DateTime(2026, 2, 3));

        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2026, 2, 1),
            new DateTime(2026, 2, 28));

        Assert.Single(result.Rows);
        Assert.Equal("anti-E", result.Rows[0].Antibody);
        Assert.Equal(3, result.Rows[0].Count);
        Assert.Equal("anti-E", result.MostCommonId);
    }

    [Fact]
    public void DateWindow_IsInclusiveAndExcludesOutOfRange()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("IN-LOW", "serum");
        iso.Db.AddSpecimen("IN-HIGH", "serum");
        iso.Db.AddSpecimen("OUT-BEFORE", "serum");
        iso.Db.AddSpecimen("OUT-AFTER", "serum");
        iso.Db.SetSpecimenFinalCall("IN-LOW", "anti-c", null, "DP", new DateTime(2026, 4, 1, 8, 0, 0));
        iso.Db.SetSpecimenFinalCall("IN-HIGH", "anti-E", null, "DP", new DateTime(2026, 4, 30, 23, 45, 0));
        iso.Db.SetSpecimenFinalCall("OUT-BEFORE", "anti-K", null, "DP", new DateTime(2026, 3, 31, 23, 59, 0));
        iso.Db.SetSpecimenFinalCall("OUT-AFTER", "anti-D", null, "DP", new DateTime(2026, 5, 1, 0, 0, 0));

        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30));

        Assert.Equal(2, result.ConfirmedSpecimens);
        Assert.Contains(result.Rows, r => r.Antibody == "anti-c");
        Assert.Contains(result.Rows, r => r.Antibody == "anti-E");
        Assert.DoesNotContain(result.Rows, r => r.Antibody == "anti-K");
        Assert.DoesNotContain(result.Rows, r => r.Antibody == "anti-D");
    }

    [Fact]
    public void MissingIdentifiedAt_ExcludedFromRangedQuery()
    {
        var specimens = new[]
        {
            new Specimen
            {
                AccessionNumber = "NO-DATE",
                FinalAntibodies = "anti-E",
                IdentifiedAt = null
            },
            new Specimen
            {
                AccessionNumber = "DATED",
                FinalAntibodies = "anti-K",
                IdentifiedAt = "2026-06-01 09:00"
            }
        };

        var ranged = AntibodyAnalyticsService.Compute(
            specimens, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(1, ranged.ConfirmedSpecimens);
        Assert.Equal("anti-K", ranged.MostCommonId);

        var allTime = AntibodyAnalyticsService.Compute(specimens, null, null);
        Assert.Equal(2, allTime.ConfirmedSpecimens);
        Assert.Equal(2, allTime.DistinctSpecificities);
    }

    [Fact]
    public void EmptyResult_IsZeroNotError()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("NONE-001", "serum");

        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31));

        Assert.Equal(0, result.ConfirmedSpecimens);
        Assert.Equal(0, result.DistinctSpecificities);
        Assert.Equal(0, result.TotalOccurrences);
        Assert.Equal("—", result.MostCommonId);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Trend);
    }

    [Fact]
    public void AllTime_IncludesInactiveSpecimens()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ACT-001", "serum", isActive: true);
        iso.Db.AddSpecimen("INACT-001", "serum", isActive: false);
        iso.Db.SetSpecimenFinalCall("ACT-001", "anti-E", null, "DP", new DateTime(2025, 1, 10));
        iso.Db.SetSpecimenFinalCall("INACT-001", "anti-K", null, "DP", new DateTime(2024, 6, 1));

        var result = AntibodyAnalyticsService.Compute(iso.Db.GetAllSpecimens(), null, null);
        Assert.Equal(2, result.ConfirmedSpecimens);
        Assert.Equal(2, result.DistinctSpecificities);
    }

    [Fact]
    public void Trend_GroupsByCalendarMonth()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("M1-A", "serum");
        iso.Db.AddSpecimen("M1-B", "serum");
        iso.Db.AddSpecimen("M2-A", "serum");
        iso.Db.SetSpecimenFinalCall("M1-A", "anti-E", null, "DP", new DateTime(2026, 1, 5));
        iso.Db.SetSpecimenFinalCall("M1-B", "anti-K", null, "DP", new DateTime(2026, 1, 20));
        iso.Db.SetSpecimenFinalCall("M2-A", "anti-c", null, "DP", new DateTime(2026, 3, 2));

        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2026, 1, 1),
            new DateTime(2026, 3, 31));

        Assert.Equal(3, result.Trend.Count);
        Assert.Equal("2026-01", result.Trend[0].MonthLabel);
        Assert.Equal(2, result.Trend[0].SpecimenCount);
        Assert.Equal("2026-02", result.Trend[1].MonthLabel);
        Assert.Equal(0, result.Trend[1].SpecimenCount);
        Assert.Equal("2026-03", result.Trend[2].MonthLabel);
        Assert.Equal(1, result.Trend[2].SpecimenCount);
    }

    [Fact]
    public void ExportCsv_WritesCountsAndRange()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("CSV-001", "serum");
        iso.Db.SetSpecimenFinalCall("CSV-001", "anti-E; anti-K", null, "DP",
            new DateTime(2026, 7, 4, 12, 0, 0));
        var result = AntibodyAnalyticsService.Compute(
            iso.Db.GetAllSpecimens(),
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31));

        var path = Path.Combine(Path.GetTempPath(), $"ab_analytics_{Guid.NewGuid():N}.csv");
        try
        {
            AntibodyAnalyticsService.ExportToCsv(result, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), path);
            var text = File.ReadAllText(path);
            Assert.Contains("Antibody", text);
            Assert.Contains("anti-E", text);
            Assert.Contains("anti-K", text);
            Assert.Contains("2026-07-01", text);
            Assert.Contains("2026-07-31", text);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AnalyticsViewModel_DefaultRangeIsLast12Months()
    {
        using var iso = new IsolatedDatabase();
        var vm = new AnalyticsViewModel(iso.Db);
        Assert.Equal(DateTime.Today.AddYears(-1), vm.FromDate);
        Assert.Equal(DateTime.Today, vm.ToDate);
        Assert.False(vm.IsAllTime);
    }

    [Fact]
    public void AnalyticsViewModel_AllTimePresetClearsDates()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("VM-001", "serum");
        iso.Db.SetSpecimenFinalCall("VM-001", "anti-E", null, "DP", new DateTime(2020, 1, 1));
        var vm = new AnalyticsViewModel(iso.Db);
        Assert.Equal(0, vm.ConfirmedSpecimens);
        vm.AllTimeCommand.Execute(null);
        Assert.True(vm.IsAllTime);
        Assert.Equal(1, vm.ConfirmedSpecimens);
        Assert.Equal("anti-E", vm.MostCommonId);
    }

    [Theory]
    [InlineData("anti-E; anti-K", 2)]
    [InlineData("anti-E, anti-c", 2)]
    [InlineData("  anti-E  ", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void FinalAntibodyParser_Split(string? text, int expected)
    {
        Assert.Equal(expected, FinalAntibodyParser.Split(text).Count);
    }
}
