using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public enum ReportType
    {
        SpecimenSummary,
        PanelSummary,
        AnalysisResults,
        AllSpecimens,
        AllPanels
    }

    public class ReportService
    {
        private readonly DatabaseService _db;

        public ReportService(DatabaseService db) => _db = db;

        // ── Text preview ──────────────────────────────────────────────────────

        public string GeneratePreviewText(ReportType type, string? specimenId = null, int? panelId = null)
        {
            return type switch
            {
                ReportType.SpecimenSummary => SpecimenSummaryText(specimenId),
                ReportType.PanelSummary => PanelSummaryText(panelId),
                ReportType.AnalysisResults => AnalysisResultsText(specimenId),
                ReportType.AllSpecimens => AllSpecimensText(),
                ReportType.AllPanels => AllPanelsText(),
                _ => string.Empty
            };
        }

        private string SpecimenSummaryText(string? specimenId)
        {
            if (specimenId == null) return "No specimen selected.";
            var s = _db.GetSpecimen(specimenId);
            if (s == null) return $"Specimen {specimenId} not found.";
            var sb = new StringBuilder();
            sb.AppendLine($"SPECIMEN SUMMARY — {s.AccessionNumber}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Type:            {s.Type}");
            sb.AppendLine($"Created:         {s.CreatedDate}");
            sb.AppendLine($"Expiration:      {s.ExpirationDate ?? "N/A"}");
            sb.AppendLine($"Last Analyzed:   {s.LastAnalyzedAt ?? "Never"}");
            sb.AppendLine();

            var panels = _db.GetSpecimenPanels(specimenId);
            sb.AppendLine($"LINKED PANELS ({panels.Count}):");
            foreach (var p in panels) sb.AppendLine($"  {p.Name} (Lot: {p.LotNumber ?? "N/A"})");
            sb.AppendLine();

            var antibodies = _db.GetSpecimenAntibodies(specimenId);
            sb.AppendLine($"SUSPECTED ANTIBODIES ({antibodies.Count}):");
            foreach (var a in antibodies) sb.AppendLine($"  {a.Antibody}  {a.Probability * 100:F1}%");
            sb.AppendLine();

            var ruleouts = _db.GetSpecimenRuleouts(specimenId);
            sb.AppendLine($"RULED-OUT ANTIBODIES ({ruleouts.Count}):");
            foreach (var r in ruleouts) sb.AppendLine($"  {r.Antibody}  (x{r.RuleoutCount})");
            return sb.ToString();
        }

        private string PanelSummaryText(int? panelId)
        {
            if (panelId == null) return "No panel selected.";
            var p = _db.GetPanel(panelId.Value);
            if (p == null) return "Panel not found.";
            var sb = new StringBuilder();
            sb.AppendLine($"PANEL SUMMARY — {p.Name}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Lot:        {p.LotNumber ?? "N/A"}");
            sb.AppendLine($"Vendor:     {p.Vendor ?? "N/A"}");
            sb.AppendLine($"Cells:      {p.NumCells}{(p.IncludeAc ? " + AC" : "")}");
            sb.AppendLine($"Expiration: {p.ExpirationDate ?? "N/A"}");
            sb.AppendLine();

            var cells = _db.GetPanelCells(panelId.Value);
            var headerAntigens = AntigenConstants.Antigens;
            sb.Append($"{"Cell",-6}");
            foreach (var ag in headerAntigens) sb.Append($" {ag,4}");
            sb.AppendLine();
            sb.AppendLine(new string('-', 6 + headerAntigens.Count * 5));
            foreach (var cell in cells)
            {
                sb.Append($"{cell.CellNumber,-6}");
                foreach (var ag in headerAntigens) sb.Append($" {cell.GetAntigen(ag),4}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string AnalysisResultsText(string? specimenId)
        {
            if (specimenId == null) return "No specimen selected.";
            var antibodies = _db.GetSpecimenAntibodies(specimenId);
            var ruleouts = _db.GetSpecimenRuleouts(specimenId);
            var sb = new StringBuilder();
            sb.AppendLine($"ANALYSIS RESULTS — {specimenId}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Suspected Antibodies: {antibodies.Count}");
            foreach (var a in antibodies) sb.AppendLine($"  {a.Antibody}  {a.Probability * 100:F1}%");
            sb.AppendLine();
            sb.AppendLine($"Ruled-out Antibodies: {ruleouts.Count}");
            foreach (var r in ruleouts) sb.AppendLine($"  {r.Antibody}  (x{r.RuleoutCount})");
            return sb.ToString();
        }

        private string AllSpecimensText()
        {
            var specimens = _db.GetAllSpecimens();
            var sb = new StringBuilder();
            sb.AppendLine($"ALL SPECIMENS ({specimens.Count})");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"{"Accession",-20} {"Type",-10} {"Created",-12} {"Expiration",-12}");
            sb.AppendLine(new string('-', 60));
            foreach (var s in specimens)
                sb.AppendLine($"{s.AccessionNumber,-20} {s.Type,-10} {s.CreatedDate,-12} {s.ExpirationDate ?? "N/A",-12}");
            return sb.ToString();
        }

        private string AllPanelsText()
        {
            var panels = _db.GetAllPanels();
            var sb = new StringBuilder();
            sb.AppendLine($"ALL PANELS ({panels.Count})");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"{"Name",-25} {"Lot",-15} {"Vendor",-15} {"Cells",-6} {"Expires",-12}");
            sb.AppendLine(new string('-', 60));
            foreach (var p in panels)
                sb.AppendLine($"{p.Name,-25} {p.LotNumber ?? "N/A",-15} {p.Vendor ?? "N/A",-15} {p.NumCells,-6} {p.ExpirationDate ?? "N/A",-12}");
            return sb.ToString();
        }

        // ── CSV Export ────────────────────────────────────────────────────────

        public void ExportToCsv(ReportType type, string filePath,
            string? specimenId = null, int? panelId = null)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture);
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, config);

            switch (type)
            {
                case ReportType.AllSpecimens:
                    csv.WriteRecords(_db.GetAllSpecimens().Select(s => new
                    {
                        s.AccessionNumber, s.Type, s.CreatedDate, s.ExpirationDate, s.LastAnalyzedAt
                    }));
                    break;
                case ReportType.AllPanels:
                    csv.WriteRecords(_db.GetAllPanels().Select(p => new
                    {
                        p.Name, p.LotNumber, p.Vendor, p.NumCells, p.ExpirationDate
                    }));
                    break;
                case ReportType.SpecimenSummary when specimenId != null:
                    var abs = _db.GetSpecimenAntibodies(specimenId);
                    var ros = _db.GetSpecimenRuleouts(specimenId);
                    csv.WriteField("Antibodies"); csv.NextRecord();
                    csv.WriteRecords(abs.Select(a => new { a.Antibody, Score = $"{a.Probability * 100:F1}%" }));
                    csv.WriteField("Ruleouts"); csv.NextRecord();
                    csv.WriteRecords(ros.Select(r => new { r.Antibody, r.RuleoutCount }));
                    break;
                case ReportType.PanelSummary when panelId.HasValue:
                    var cells = _db.GetPanelCells(panelId.Value);
                    // Header row
                    csv.WriteField("Cell");
                    foreach (var ag in AntigenConstants.Antigens) csv.WriteField(ag);
                    csv.NextRecord();
                    foreach (var cell in cells)
                    {
                        csv.WriteField(cell.CellNumber);
                        foreach (var ag in AntigenConstants.Antigens) csv.WriteField(cell.GetAntigen(ag));
                        csv.NextRecord();
                    }
                    break;
                default:
                    writer.Write(GeneratePreviewText(type, specimenId, panelId));
                    break;
            }
        }

        // ── PDF Export ────────────────────────────────────────────────────────

        public void ExportToPdf(ReportType type, string filePath,
            string? specimenId = null, int? panelId = null)
        {
            var text = GeneratePreviewText(type, specimenId, panelId);
            var doc = new PdfDocument();
            doc.Info.Title = type.ToString();
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.Letter;
            XGraphics? gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Courier New", 9);
            double x = 40, y = 40, lineH = 13;
            foreach (var line in text.Split('\n'))
            {
                if (y > page.Height.Point - 50)
                {
                    gfx?.Dispose();
                    page = doc.AddPage();
                    page.Size = PdfSharp.PageSize.Letter;
                    gfx = XGraphics.FromPdfPage(page);
                    y = 40;
                }
                gfx!.DrawString(line.TrimEnd(), font, XBrushes.Black,
                    new XRect(x, y, page.Width.Point - 80, lineH), XStringFormats.TopLeft);
                y += lineH;
            }
            gfx?.Dispose();
            doc.Save(filePath);
            doc.Dispose();
        }
    }
}
