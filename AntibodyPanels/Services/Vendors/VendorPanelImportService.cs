using System;
using System.Linq;
using AntibodyPanels.Data;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class VendorPanelImportService
    {
        private readonly DatabaseService _db;

        public VendorPanelImportService(DatabaseService db)
        {
            _db = db;
        }

        public int Persist(VendorParseResult parsed, bool replaceExisting = false)
        {
            if (!parsed.Success)
                throw new InvalidOperationException(
                    "Vendor panel did not parse:\n" + string.Join("\n", parsed.Errors));

            var existing = _db.FindPanelByVendorLot(parsed.Vendor, parsed.LotNumber);
            if (existing != null && !replaceExisting)
                throw new InvalidOperationException(
                    $"A {parsed.Vendor} panel with lot {parsed.LotNumber} is already in the database (panel #{existing.PanelId}).");

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

            var importedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var id = _db.AddPanel(
                parsed.Name,
                parsed.LotNumber,
                parsed.Vendor,
                numCells,
                parsed.ExpirationDate,
                includeAc,
                startCell,
                isActive: null,
                catalogNumber: parsed.CatalogNumber,
                productLine: parsed.ProductLine,
                enzymeTreated: parsed.EnzymeTreated,
                sourceUrl: parsed.SourceUrl,
                sourceFormat: parsed.SourceFormat,
                importedAt: importedAt,
                specialNotes: parsed.SpecialNotes);

            _db.ReplacePanelCells(id, parsed.Cells);
            if (parsed.AntigenOrder.Count > 0)
                _db.SetPanelAntigenOrder(id, parsed.AntigenOrder);
            return id;
        }
    }
}
