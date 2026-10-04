using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class VendorPanelImportService
    {
        private readonly DatabaseService _db;
        private readonly ImportArtifactStore _artifacts;

        public VendorPanelImportService(DatabaseService db, ImportArtifactStore? artifacts = null)
        {
            _db = db;
            _artifacts = artifacts ?? new ImportArtifactStore(db.ArtifactDirectory);
        }

        public int Persist(VendorParseResult parsed, bool replaceExisting = false) =>
            Import(parsed, replaceExisting).PanelId;

        public PanelImportOutcome Import(VendorParseResult parsed, bool replaceExisting = false)
        {
            if (!parsed.Success)
                throw new InvalidOperationException(
                    "Vendor panel did not parse:\n" + string.Join("\n", parsed.Errors));

            var existing = _db.FindPanelByVendorLot(parsed.Vendor, parsed.LotNumber);
            if (existing != null && !replaceExisting)
                throw new InvalidOperationException(
                    $"A {parsed.Vendor} panel with lot {parsed.LotNumber} is already in the database (panel #{existing.PanelId}).");

            var prior = _db.FindPriorVendorPanel(parsed.Vendor, parsed.ProductLine, parsed.LotNumber);
            if (existing != null)
                _db.DeletePanel(existing.PanelId);

            var includeAc = parsed.Cells.Any(c =>
                string.Equals(c.CellNumber, "AC", StringComparison.OrdinalIgnoreCase));
            var numCells = parsed.Cells.Count(c =>
                !string.Equals(c.CellNumber, "AC", StringComparison.OrdinalIgnoreCase));
            var startCell = 1;
            foreach (var cell in parsed.Cells)
            {
                if (int.TryParse(cell.CellNumber, out var n))
                {
                    startCell = n;
                    break;
                }
            }

            string? sha = null;
            string? artifactPath = null;
            if (parsed.SourceBytes is { Length: > 0 })
            {
                sha = PanelImportReviewer.Hash(parsed.SourceBytes);
                artifactPath = _artifacts.Save(parsed.SourceBytes, parsed.Vendor, parsed.LotNumber,
                    parsed.SourceFormat, sha);
            }

            var importedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var id = _db.AddPanel(
                parsed.Name,
                parsed.LotNumber,
                parsed.Vendor,
                numCells,
                parsed.ExpirationDate,
                includeAc,
                startCell,
                isActive: false,
                catalogNumber: parsed.CatalogNumber,
                productLine: parsed.ProductLine,
                enzymeTreated: parsed.EnzymeTreated,
                sourceUrl: parsed.SourceUrl,
                sourceFormat: parsed.SourceFormat,
                importedAt: importedAt,
                specialNotes: parsed.SpecialNotes,
                sourceSha256: sha,
                sourceArtifactPath: artifactPath);

            _db.ReplacePanelCells(id, parsed.Cells);
            if (parsed.AntigenOrder.Count > 0)
                _db.SetPanelAntigenOrder(id, parsed.AntigenOrder);

            var incoming = IncomingAntigens(parsed);
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
                Schema = PanelImportReviewer.CompareSchema(incoming, priorAntigens),
                StoredInactive = true,
                TypingIssues = PanelTypingInspector.Inspect(parsed.Cells, parsed.UnknownAntigens)
            };
            review.Explanation = PanelImportReviewer.Explain(review, parsed.LotNumber, parsed.Vendor);
            return new PanelImportOutcome { PanelId = id, Review = review };
        }

        private static IReadOnlyList<string> IncomingAntigens(VendorParseResult parsed)
        {
            if (parsed.AntigenOrder.Count > 0)
                return parsed.AntigenOrder;
            return parsed.Cells
                .SelectMany(c => AntigenConstants.AllKnownAntigens.Where(c.HasTypedAntigen))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
