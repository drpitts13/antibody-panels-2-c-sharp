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
        AllPanels,
        ClinicalIdentification,
        PanelAntigram
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
                ReportType.ClinicalIdentification => ClinicalIdentificationText(specimenId),
                ReportType.PanelAntigram => PanelAntigramText(panelId),
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
            if (!string.IsNullOrWhiteSpace(s.Phenotype))
                sb.AppendLine($"Phenotype:       {s.Phenotype}");
            if (!string.IsNullOrWhiteSpace(s.PreviousAntibodies))
                sb.AppendLine($"Previous Abs:    {s.PreviousAntibodies}");
            if (!string.IsNullOrWhiteSpace(s.DatResult))
                sb.AppendLine($"DAT:             {s.DatResult}");
            if (!string.IsNullOrWhiteSpace(s.Notes))
                sb.AppendLine($"Notes:           {s.Notes}");
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
            AppendAntigenGrid(sb, _db.GetPanelCells(panelId.Value));
            return sb.ToString();
        }

        private string PanelAntigramText(int? panelId)
        {
            if (panelId == null) return "No panel selected.";
            var p = _db.GetPanel(panelId.Value);
            if (p == null) return "Panel not found.";
            var sb = new StringBuilder();
            var lab = AppSettings.Current.LabName;
            sb.AppendLine(lab.ToUpperInvariant());
            sb.AppendLine("PANEL ANTIGRAM");
            sb.AppendLine($"{p.Name}   Lot: {p.LotNumber ?? "N/A"}   Vendor: {p.Vendor ?? "N/A"}   Exp: {p.ExpirationDate ?? "N/A"}");
            sb.AppendLine(new string('=', 78));
            AppendAntigenGrid(sb, _db.GetPanelCells(panelId.Value));
            return sb.ToString();
        }

        private static void AppendAntigenGrid(StringBuilder sb, IReadOnlyList<PanelCell> cells)
        {
            var headerAntigens = AntigenConstants.GetAnalyzedAntigens(
                PanelCsvService.ExtraAntigensOnCells(cells));
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
        }

        private string AnalysisResultsText(string? specimenId)
        {
            if (specimenId == null) return "No specimen selected.";
            var antibodies = _db.GetSpecimenAntibodies(specimenId);
            var ruleouts = _db.GetSpecimenRuleouts(specimenId);
            var runs = _db.GetAllSpecimenRuns(specimenId);
            var sb = new StringBuilder();
            sb.AppendLine($"ANALYSIS RESULTS — {specimenId}");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Suspected Antibodies: {antibodies.Count}");
            foreach (var a in antibodies) sb.AppendLine($"  {a.Antibody}  {a.Probability * 100:F1}%");
            sb.AppendLine();
            sb.AppendLine($"Ruled-out Antibodies: {ruleouts.Count}");
            foreach (var r in ruleouts) sb.AppendLine($"  {r.Antibody}  (x{r.RuleoutCount})");

            if (runs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"PANEL RUNS ({runs.Count}):");
                foreach (var run in runs)
                    sb.AppendLine($"  [{run.PanelName}] {run.DisplayLabel}");
            }
            return sb.ToString();
        }

        private string ClinicalIdentificationText(string? specimenId)
        {
            if (specimenId == null) return "No specimen selected.";
            var s = _db.GetSpecimen(specimenId);
            if (s == null) return $"Specimen {specimenId} not found.";

            var settings = AppSettings.Current;
            var analyzer = new AntibodyAnalyzer(_db);
            var analysis = analyzer.AnalyzeSpecimen(specimenId, updateDb: false);
            var runs = _db.GetAllSpecimenRuns(specimenId);
            var sb = new StringBuilder();

            sb.AppendLine(settings.LabName.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(settings.Department))
                sb.AppendLine(settings.Department);
            sb.AppendLine("ANTIBODY IDENTIFICATION WORKSHEET");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine($"Accession: {s.AccessionNumber,-16} Type: {s.Type,-10} Date: {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine($"Phenotype: {s.Phenotype ?? "N/A"}");
            sb.AppendLine($"Previous antibodies: {s.PreviousAntibodies ?? "N/A"}");
            sb.AppendLine($"DAT: {s.DatResult ?? "NT"}");
            if (!string.IsNullOrWhiteSpace(s.Notes))
                sb.AppendLine($"Notes: {s.Notes}");
            sb.AppendLine();

            foreach (var run in runs)
            {
                var panel = _db.GetPanel(run.PanelId);
                var cells = _db.GetPanelCells(run.PanelId);
                var rxns = _db.GetReactions(run.RunId).ToDictionary(r => r.CellNumber);
                sb.AppendLine($"Panel: {panel?.Name ?? run.PanelName}   Lot: {panel?.LotNumber ?? "N/A"}   Vendor: {panel?.Vendor ?? "N/A"}");
                sb.AppendLine($"Run: {run.DisplayLabel}");

                var antigens = AntigenConstants.GetAnalyzedAntigens(
                    PanelCsvService.ExtraAntigensOnCells(cells));
                sb.Append($"{"Cell",-5}");
                foreach (var ag in antigens)
                    sb.Append(ag.PadLeft(Math.Max(3, ag.Length) + 1));
                sb.Append("  IS   37   AHG  CC");
                sb.AppendLine();
                sb.AppendLine(new string('-', 5 + antigens.Sum(a => Math.Max(3, a.Length) + 1) + 22));

                foreach (var cell in cells)
                {
                    rxns.TryGetValue(cell.CellNumber, out var rxn);
                    sb.Append($"{cell.CellNumber,-5}");
                    foreach (var ag in antigens)
                    {
                        var w = Math.Max(3, ag.Length) + 1;
                        var v = cell.GetAntigen(ag);
                        sb.Append(v.PadLeft(w));
                    }
                    sb.Append($"  {(rxn?.IS ?? "NT"),-4} {(rxn?.C37 ?? "NT"),-4} {(rxn?.AHG ?? "NT"),-4} {rxn?.CC ?? "NT"}");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            sb.AppendLine("INTERPRETATION");
            sb.AppendLine(new string('-', 40));
            if (s.HasFinalCall)
            {
                sb.AppendLine("FINAL IDENTIFICATION (confirmed)");
                sb.AppendLine($"  {s.FinalAntibodies}");
                sb.AppendLine($"  Confirmed by {s.IdentifiedBy ?? "—"} on {s.IdentifiedAt ?? "—"}");
                if (!string.IsNullOrWhiteSpace(s.FinalComment))
                    sb.AppendLine($"  Comment: {s.FinalComment}");
                sb.AppendLine();
                sb.AppendLine("Analyzer suspected (for reference):");
                if (analysis.Suspected.Count > 0)
                {
                    foreach (var (ab, prob) in analysis.Suspected.OrderByDescending(x => x.Value))
                        sb.AppendLine(FormatSuspectedAntibodyLine(ab, prob, analysis));
                }
                else
                {
                    sb.AppendLine("  None.");
                }
            }
            else if (analysis.Suspected.Count > 0)
            {
                sb.AppendLine("Suspected antibodies (unconfirmed):");
                foreach (var (ab, prob) in analysis.Suspected.OrderByDescending(x => x.Value))
                    sb.AppendLine(FormatSuspectedAntibodyLine(ab, prob, analysis));
            }
            else
            {
                sb.AppendLine("No antibodies suspected based on current reactions.");
            }
            sb.AppendLine();
            if (analysis.RuledOut.Count > 0)
            {
                sb.AppendLine("Ruled out:");
                sb.AppendLine("  " + string.Join(", ", analysis.RuledOut.Keys.OrderBy(x => x)));
            }
            if (analysis.Suggestions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Suggestions:");
                foreach (var sug in analysis.Suggestions)
                    sb.AppendLine($"  • {sug}");
            }

            sb.AppendLine();
            sb.AppendLine(new string('_', 78));
            sb.AppendLine("Technologist: ___________________________  Date: ______________");
            sb.AppendLine();
            sb.AppendLine("Supervisor:   ___________________________  Date: ______________");
            return sb.ToString();
        }

        private static string FormatSuspectedAntibodyLine(string antibody, double probability,
            AnalysisResult analysis)
        {
            analysis.SuspectedStatistics.TryGetValue(antibody, out var stats);
            var id = stats != null ? $"  {stats.IdentificationDetail}" : "";
            return $"  {antibody}  {probability * 100:F1}%{id}";
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
                    var antigens = AntigenConstants.GetAnalyzedAntigens(
                        PanelCsvService.ExtraAntigensOnCells(cells));
                    csv.WriteField("Cell");
                    foreach (var ag in antigens) csv.WriteField(ag);
                    csv.NextRecord();
                    foreach (var cell in cells)
                    {
                        csv.WriteField(cell.CellNumber);
                        foreach (var ag in antigens) csv.WriteField(cell.GetAntigen(ag));
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
            bool landscape = type is ReportType.ClinicalIdentification or ReportType.PanelAntigram;
            if (landscape)
                page.Orientation = PdfSharp.PageOrientation.Landscape;
            XGraphics? gfx = XGraphics.FromPdfPage(page);
            var fontSize = landscape ? 7 : 9;
            var font = new XFont("Courier New", fontSize);
            double x = 28, y = 28, lineH = landscape ? 10 : 13;
            double bottom = page.Height.Point - 36;
            foreach (var line in text.Split('\n'))
            {
                if (y > bottom)
                {
                    gfx?.Dispose();
                    page = doc.AddPage();
                    page.Size = PdfSharp.PageSize.Letter;
                    if (landscape)
                        page.Orientation = PdfSharp.PageOrientation.Landscape;
                    gfx = XGraphics.FromPdfPage(page);
                    y = 28;
                }
                gfx!.DrawString(line.TrimEnd(), font, XBrushes.Black,
                    new XRect(x, y, page.Width.Point - 56, lineH), XStringFormats.TopLeft);
                y += lineH;
            }
            gfx?.Dispose();
            doc.Save(filePath);
            doc.Dispose();
        }
    }
}
