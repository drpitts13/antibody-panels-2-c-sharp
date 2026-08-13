using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

[Collection("PersistentDatabase")]
public class ReportAccuracyTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public ReportAccuracyTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void SpecimenSummary_ContainsAccessionAndLinkedPanels()
    {
        _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var text = _fixture.Reports.GeneratePreviewText(ReportType.SpecimenSummary, "2024-001");

        Assert.Contains("2024-001", text);
        Assert.Contains("SPECIMEN SUMMARY", text);
        Assert.Contains("serum", text);
        Assert.Contains("Ortho Resolve Panel A", text);
        Assert.Contains("SUSPECTED ANTIBODIES", text);
        Assert.Contains("RULED-OUT ANTIBODIES", text);
    }

    [Fact]
    public void AnalysisResults_MatchesDatabaseAntibodies()
    {
        _fixture.Analyzer.AnalyzeSpecimen("2024-003", updateDb: true);
        var antibodies = _fixture.Db.GetSpecimenAntibodies("2024-003");
        var text = _fixture.Reports.GeneratePreviewText(ReportType.AnalysisResults, "2024-003");

        Assert.Contains("ANALYSIS RESULTS", text);
        Assert.Contains($"Suspected Antibodies: {antibodies.Count}", text);
        foreach (var ab in antibodies)
        {
            Assert.Contains(ab.Antibody, text);
            Assert.Contains($"{ab.Probability * 100:F1}%", text);
        }
    }

    [Fact]
    public void PanelSummary_ContainsAllCellsAndAntigens()
    {
        var ortho = _fixture.Db.GetAllPanels().First(p => p.LotNumber == "ORT2024A");
        var text = _fixture.Reports.GeneratePreviewText(ReportType.PanelSummary, panelId: ortho.PanelId);

        Assert.Contains("PANEL SUMMARY", text);
        Assert.Contains(ortho.Name, text);
        Assert.Contains("ORT2024A", text);
        Assert.Contains("Cell", text);
        foreach (var cell in _fixture.Db.GetPanelCells(ortho.PanelId))
            Assert.Contains(cell.CellNumber, text);
    }

    [Fact]
    public void AllSpecimensReport_ListsEverySpecimen()
    {
        var specimens = _fixture.Db.GetAllSpecimens();
        var text = _fixture.Reports.GeneratePreviewText(ReportType.AllSpecimens);
        Assert.Contains($"ALL SPECIMENS ({specimens.Count})", text);
        foreach (var s in specimens)
            Assert.Contains(s.AccessionNumber, text);
    }

    [Fact]
    public void AllPanelsReport_ListsEveryPanel()
    {
        var panels = _fixture.Db.GetAllPanels();
        var text = _fixture.Reports.GeneratePreviewText(ReportType.AllPanels);
        Assert.Contains($"ALL PANELS ({panels.Count})", text);
        foreach (var p in panels)
            Assert.Contains(p.Name, text);
    }

    [Fact]
    public void CsvExport_AllSpecimens_MatchesDatabaseCount()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"specimens_{Guid.NewGuid():N}.csv");
        try
        {
            _fixture.Reports.ExportToCsv(ReportType.AllSpecimens, csvPath);
            var lines = File.ReadAllLines(csvPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            Assert.Equal(_fixture.Db.GetAllSpecimens().Count + 1, lines.Count); // header + rows
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void CsvExport_PanelSummary_ContainsAntigenHeader()
    {
        var ortho = _fixture.Db.GetAllPanels().First();
        var csvPath = Path.Combine(Path.GetTempPath(), $"panel_{Guid.NewGuid():N}.csv");
        try
        {
            _fixture.Reports.ExportToCsv(ReportType.PanelSummary, csvPath, panelId: ortho.PanelId);
            var header = File.ReadLines(csvPath).First();
            Assert.Contains("Cell", header);
            Assert.Contains("D", header);
            Assert.Contains("P1", header);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    [Fact]
    public void PdfExport_CreatesValidPdfFile()
    {
        _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var pdfPath = Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid():N}.pdf");
        try
        {
            _fixture.Reports.ExportToPdf(ReportType.SpecimenSummary, pdfPath, "2024-001");
            Assert.True(File.Exists(pdfPath));
            Assert.True(new FileInfo(pdfPath).Length > 500);
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    [Fact]
    public void ReportForMissingSpecimen_ReturnsNotFoundMessage()
    {
        var text = _fixture.Reports.GeneratePreviewText(ReportType.SpecimenSummary, "NONEXISTENT-999");
        Assert.Contains("not found", text, StringComparison.OrdinalIgnoreCase);
    }
}
