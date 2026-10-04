using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public sealed class LabPanelImportRequest
    {
        public string Name { get; init; } = string.Empty;
        public string? LotNumber { get; init; }
        public string? Vendor { get; init; }
        public string? ExpirationDate { get; init; }
        public string? CatalogNumber { get; init; }
        public string? ProductLine { get; init; }
        public bool EnzymeTreated { get; init; }
        public bool IncludeAc { get; init; }
        public int NumCells { get; init; }
        public int StartCell { get; init; } = 1;
    }

    public sealed class LabPanelImportService
    {
        private readonly DatabaseService _db;
        private readonly ImportArtifactStore _artifacts;

        public LabPanelImportService(DatabaseService db, ImportArtifactStore? artifacts = null)
        {
            _db = db;
            _artifacts = artifacts ?? new ImportArtifactStore(db.ArtifactDirectory);
        }

        public PanelImportOutcome Import(PanelCsvImportResult parsed, LabPanelImportRequest request,
            bool replaceExisting = false)
        {
            if (!parsed.Success)
                throw new InvalidOperationException(
                    "Lab panel CSV did not parse:\n" + string.Join("\n", parsed.Errors));

            var existing = _db.FindPanelByVendorLot(request.Vendor, request.LotNumber);
            if (existing != null && !replaceExisting)
                throw new InvalidOperationException(
                    $"A panel with lot {request.LotNumber} from {request.Vendor} is already stored (panel #{existing.PanelId}).");

            var prior = _db.FindPriorVendorPanel(request.Vendor, request.ProductLine, request.LotNumber);
            if (existing != null)
                _db.DeletePanel(existing.PanelId);

            string? sha = null;
            string? artifactPath = null;
            if (parsed.SourceBytes is { Length: > 0 })
            {
                sha = PanelImportReviewer.Hash(parsed.SourceBytes);
                artifactPath = _artifacts.Save(parsed.SourceBytes, request.Vendor, request.LotNumber,
                    GuessFormat(parsed.SourceFileName), sha);
            }

            var importedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var id = _db.AddPanel(
                request.Name,
                request.LotNumber,
                request.Vendor,
                request.NumCells,
                request.ExpirationDate,
                request.IncludeAc,
                request.StartCell,
                isActive: false,
                catalogNumber: request.CatalogNumber,
                productLine: request.ProductLine,
                enzymeTreated: request.EnzymeTreated,
                sourceUrl: parsed.SourcePath,
                sourceFormat: GuessFormat(parsed.SourceFileName),
                importedAt: importedAt,
                sourceSha256: sha,
                sourceArtifactPath: artifactPath);

            var storedCells = ToCells(parsed);
            _db.ReplacePanelCells(id, storedCells);
            if (parsed.AntigenHeaderOrder.Count > 0)
                _db.SetPanelAntigenOrder(id, parsed.AntigenHeaderOrder);

            IReadOnlyList<string> priorAntigens = prior == null
                ? Array.Empty<string>()
                : _db.GetPanelTypedAntigens(prior.PanelId);
            var review = new PanelImportReview
            {
                PanelId = id,
                Sha256 = sha,
                ArtifactPath = artifactPath,
                PriorLotNumber = prior?.LotNumber,
                PriorPanelId = prior?.PanelId,
                Schema = PanelImportReviewer.CompareSchema(parsed.AntigenHeaderOrder, priorAntigens),
                StoredInactive = true,
                TypingIssues = PanelTypingInspector.Inspect(storedCells, parsed.UnknownHeaders)
            };
            review.Explanation = PanelImportReviewer.Explain(review, request.LotNumber, request.Vendor);
            return new PanelImportOutcome { PanelId = id, Review = review };
        }

        private static string GuessFormat(string? fileName)
        {
            var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
            return ext is "json" or "xml" or "xlsx" or "pdf" ? ext : "csv";
        }

        public static List<PanelCell> ToCells(PanelCsvImportResult parsed) =>
            parsed.Cells.Select(c =>
            {
                var cell = new PanelCell { CellNumber = c.CellNumber };
                foreach (var (ag, val) in c.Antigens)
                    cell.SetAntigen(ag, val);
                return cell;
            }).ToList();
    }
}
