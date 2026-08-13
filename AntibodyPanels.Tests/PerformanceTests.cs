using System.Diagnostics;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class PerformanceTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public PerformanceTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAllSpecimens_CompletesUnder500ms()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            _ = _fixture.Db.GetAllSpecimens();
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500, $"100 specimen queries took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void GetAllPanels_CompletesUnder500ms()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
            _ = _fixture.Db.GetAllPanels();
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500, $"100 panel queries took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void AnalyzeSpecimen_SingleSpecimen_CompletesUnder2Seconds()
    {
        var sw = Stopwatch.StartNew();
        _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: false);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Analysis took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void AnalyzeSpecimen_AllSeededSpecimensWithReactions_CompletesUnder10Seconds()
    {
        var specimens = new[] { "2024-001", "2024-003", "2024-009", "TEST-NO-AB", "TEST-MULTI-AB" };
        var sw = Stopwatch.StartNew();
        foreach (var id in specimens)
            _fixture.Analyzer.AnalyzeSpecimen(id, updateDb: false);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 10000, $"Batch analysis took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ReportGeneration_AllTypes_CompletesUnder3Seconds()
    {
        _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var ortho = _fixture.Db.GetAllPanels().First();

        var sw = Stopwatch.StartNew();
        _fixture.Reports.GeneratePreviewText(Services.ReportType.AllSpecimens);
        _fixture.Reports.GeneratePreviewText(Services.ReportType.AllPanels);
        _fixture.Reports.GeneratePreviewText(Services.ReportType.SpecimenSummary, "2024-001");
        _fixture.Reports.GeneratePreviewText(Services.ReportType.AnalysisResults, "2024-001");
        _fixture.Reports.GeneratePreviewText(Services.ReportType.PanelSummary, panelId: ortho.PanelId);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Report generation took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void SearchCellsByProfile_CompletesUnder1Second()
    {
        var criteria = new Dictionary<string, string> { { "D", "+" }, { "E", "-" } };
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            _fixture.Db.SearchCellsByProfile(criteria);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"50 searches took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void BulkAnalysis_50Specimens_CompletesUnder30Seconds()
    {
        using var iso = new IsolatedDatabase();
        var panelId = iso.Db.AddPanel("Perf Panel", "L", "V", 11, null, false);
        var cells = iso.Db.GetPanelCells(panelId);
        foreach (var cell in cells)
            iso.Db.UpdatePanelCellAntigen(cell.Id, "E", "+");

        for (int i = 0; i < 50; i++)
        {
            var acc = $"PERF-{i:D3}";
            iso.Db.AddSpecimen(acc, "serum", null);
            iso.Db.LinkSpecimenPanel(acc, panelId);
            foreach (var cell in cells)
                iso.Db.SaveReaction(acc, panelId, cell.CellNumber, "0", "0", "2+", "2+");
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            iso.Analyzer.AnalyzeSpecimen($"PERF-{i:D3}", updateDb: false);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 30000, $"50 analyses took {sw.ElapsedMilliseconds}ms");
    }
}
