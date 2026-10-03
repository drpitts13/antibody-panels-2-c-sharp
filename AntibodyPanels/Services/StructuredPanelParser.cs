using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AntibodyPanels.Models;
using AntibodyPanels.Services.Vendors;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Imports a lab- or vendor-authored panel from JSON or XML without
    /// inventing antigen types that the file did not list.
    /// </summary>
    public static class StructuredPanelParser
    {
        public static VendorParseResult ParseJson(byte[] bytes, string vendor, string fileName,
            VendorLotListing? listing)
        {
            try
            {
                using var doc = JsonDocument.Parse(DecodeText(bytes));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    return Fail(vendor, listing, fileName, "JSON root must be a panel object, not an array.");
                return FromDocument(ReadJsonPanel(root), vendor, fileName, listing);
            }
            catch (JsonException ex)
            {
                return Fail(vendor, listing, fileName, "Invalid JSON: " + ex.Message);
            }
        }

        public static VendorParseResult ParseXml(byte[] bytes, string vendor, string fileName,
            VendorLotListing? listing)
        {
            try
            {
                var doc = XDocument.Parse(DecodeText(bytes));
                var root = doc.Root ?? throw new InvalidOperationException("XML has no root.");
                return FromDocument(ReadXmlPanel(root), vendor, fileName, listing);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                return Fail(vendor, listing, fileName, "Invalid XML: " + ex.Message);
            }
        }

        public static PanelCsvImportResult ToLabResult(VendorParseResult parsed)
        {
            var result = new PanelCsvImportResult();
            result.Errors.AddRange(parsed.Errors);
            foreach (var ag in parsed.AntigenOrder)
            {
                if (!result.AntigenHeaderOrder.Contains(ag))
                    result.AntigenHeaderOrder.Add(ag);
            }
            foreach (var cell in parsed.Cells)
            {
                var imported = new ImportedPanelCell { CellNumber = cell.CellNumber };
                foreach (var pair in cell.Antigens)
                {
                    if (!AntigenConstants.IsTypedAntigenValue(pair.Value)) continue;
                    imported.Antigens[pair.Key] = pair.Value;
                    if (!result.AntigenHeaderOrder.Contains(pair.Key))
                        result.AntigenHeaderOrder.Add(pair.Key);
                }
                result.Cells.Add(imported);
            }
            if (result.Cells.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add("Structured file contains no panel cells.");
            return result;
        }

        private static VendorParseResult FromDocument(StructuredPanelFile file, string vendor,
            string fileName, VendorLotListing? listing)
        {
            var resolvedVendor = FirstNonEmpty(file.Vendor, listing?.Vendor, vendor) ?? vendor;
            var result = new VendorParseResult
            {
                Vendor = resolvedVendor,
                Name = FirstNonEmpty(file.Name, listing?.ProductLine, BuildName(resolvedVendor, file.ProductLine, file.LotNumber))
                    ?? resolvedVendor + " panel",
                LotNumber = FirstNonEmpty(file.LotNumber, listing?.LotNumber),
                ExpirationDate = FirstNonEmpty(file.ExpirationDate, listing?.ExpirationDate),
                CatalogNumber = FirstNonEmpty(file.CatalogNumber, listing?.CatalogNumber),
                ProductLine = FirstNonEmpty(file.ProductLine, listing?.ProductLine),
                EnzymeTreated = file.EnzymeTreated || listing?.EnzymeTreated == true,
                SourceUrl = listing?.DownloadUrl,
                SourceFormat = GuessFormat(fileName),
                SpecialNotes = listing?.Notes
            };

            var order = new List<string>();
            foreach (var raw in file.AntigenOrder)
            {
                var ag = VendorAntigenAliases.Resolve(raw);
                if (ag != null && !order.Contains(ag)) order.Add(ag);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in file.Cells)
            {
                if (string.IsNullOrWhiteSpace(src.CellNumber))
                {
                    result.Errors.Add("A cell is missing cellNumber.");
                    continue;
                }
                if (!seen.Add(src.CellNumber))
                {
                    result.Errors.Add($"Duplicate cell '{src.CellNumber}'.");
                    continue;
                }
                var cell = new PanelCell
                {
                    CellNumber = src.CellNumber.Trim(),
                    DonorId = EmptyToNull(src.DonorId),
                    RhPhenotype = EmptyToNull(src.RhPhenotype)
                };
                foreach (var pair in src.Antigens)
                {
                    var ag = VendorAntigenAliases.Resolve(pair.Key);
                    if (ag == null) continue;
                    var required = AntigenConstants.IsStandard(ag);
                    var value = VendorAntigenAliases.NormalizeValue(pair.Value, required);
                    if (value == null)
                    {
                        if (required && !string.IsNullOrWhiteSpace(pair.Value))
                            result.Errors.Add($"Cell {cell.CellNumber}: invalid value '{pair.Value}' for {ag}.");
                        continue;
                    }
                    cell.SetAntigen(ag, value);
                    if (!order.Contains(ag)) order.Add(ag);
                }
                result.Cells.Add(cell);
            }

            result.AntigenOrder.AddRange(order);
            if (result.Cells.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add("Structured file contains no panel cells.");
            return result;
        }

        private static StructuredPanelFile ReadJsonPanel(JsonElement root)
        {
            var file = new StructuredPanelFile
            {
                Vendor = ReadString(root, "vendor"),
                Name = ReadString(root, "name"),
                LotNumber = ReadString(root, "lotNumber", "lot"),
                ExpirationDate = ReadString(root, "expirationDate", "expiration"),
                CatalogNumber = ReadString(root, "catalogNumber", "catalog"),
                ProductLine = ReadString(root, "productLine"),
                EnzymeTreated = ReadBool(root, "enzymeTreated")
            };
            if (TryGetProperty(root, out var orderEl, "antigenOrder", "antigens")
                && orderEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in orderEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        file.AntigenOrder.Add(item.GetString() ?? "");
                    else if (TryGetProperty(item, out var nameEl, "name") && nameEl.ValueKind == JsonValueKind.String)
                        file.AntigenOrder.Add(nameEl.GetString() ?? "");
                }
            }
            if (TryGetProperty(root, out var cellsEl, "cells") && cellsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var cellEl in cellsEl.EnumerateArray())
                    file.Cells.Add(ReadJsonCell(cellEl));
            }
            return file;
        }

        private static StructuredCell ReadJsonCell(JsonElement cellEl)
        {
            var cell = new StructuredCell
            {
                CellNumber = ReadString(cellEl, "cellNumber", "cell", "donor") ?? "",
                DonorId = ReadString(cellEl, "donorId", "donor"),
                RhPhenotype = ReadString(cellEl, "rhPhenotype", "phenotype")
            };
            if (TryGetProperty(cellEl, out var agEl, "antigens") && agEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in agEl.EnumerateObject())
                    cell.Antigens[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
            }
            return cell;
        }

        private static StructuredPanelFile ReadXmlPanel(XElement root)
        {
            var file = new StructuredPanelFile
            {
                Vendor = AttrOrChild(root, "vendor"),
                Name = AttrOrChild(root, "name"),
                LotNumber = AttrOrChild(root, "lotNumber", "lot"),
                ExpirationDate = AttrOrChild(root, "expirationDate", "expiration"),
                CatalogNumber = AttrOrChild(root, "catalogNumber", "catalog"),
                ProductLine = AttrOrChild(root, "productLine"),
                EnzymeTreated = bool.TryParse(AttrOrChild(root, "enzymeTreated"), out var enz) && enz
            };
            var orderEl = Child(root, "antigenOrder", "antigens");
            if (orderEl != null)
            {
                foreach (var ag in orderEl.Elements().Where(e =>
                    e.Name.LocalName.Equals("antigen", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("ag", StringComparison.OrdinalIgnoreCase)))
                {
                    file.AntigenOrder.Add((string?)ag.Attribute("name") ?? ag.Value);
                }
            }
            var cellsEl = Child(root, "cells") ?? root;
            foreach (var cellEl in cellsEl.Elements().Where(e =>
                e.Name.LocalName.Equals("cell", StringComparison.OrdinalIgnoreCase)))
            {
                var cell = new StructuredCell
                {
                    CellNumber = AttrOrChild(cellEl, "cellNumber", "cell", "donor") ?? "",
                    DonorId = AttrOrChild(cellEl, "donorId", "donor"),
                    RhPhenotype = AttrOrChild(cellEl, "rhPhenotype", "phenotype")
                };
                foreach (var ag in cellEl.Elements().Where(e =>
                    e.Name.LocalName.Equals("antigen", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("ag", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = (string?)ag.Attribute("name") ?? (string?)ag.Attribute("id");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    cell.Antigens[name] = ag.Value;
                }
                file.Cells.Add(cell);
            }
            return file;
        }

        private static VendorParseResult Fail(string vendor, VendorLotListing? listing,
            string fileName, string error)
        {
            var result = new VendorParseResult
            {
                Vendor = vendor,
                Name = listing?.ProductLine ?? vendor + " panel",
                LotNumber = listing?.LotNumber,
                ExpirationDate = listing?.ExpirationDate,
                CatalogNumber = listing?.CatalogNumber,
                ProductLine = listing?.ProductLine,
                SourceFormat = GuessFormat(fileName)
            };
            result.Errors.Add(error);
            return result;
        }

        private static string GuessFormat(string fileName)
        {
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "json";
            if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return "xml";
            if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return "xlsx";
            return "structured";
        }

        private static string BuildName(string vendor, string? product, string? lot)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(product)) parts.Add(product.Trim());
            else parts.Add(vendor + " panel");
            if (!string.IsNullOrWhiteSpace(lot)) parts.Add(lot.Trim());
            return string.Join(" ", parts);
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string? EmptyToNull(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string? ReadString(JsonElement el, params string[] names)
        {
            if (!TryGetProperty(el, out var prop, names)) return null;
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        private static bool ReadBool(JsonElement el, params string[] names)
        {
            if (!TryGetProperty(el, out var prop, names)) return false;
            return prop.ValueKind == JsonValueKind.True
                || (prop.ValueKind == JsonValueKind.String
                    && bool.TryParse(prop.GetString(), out var b) && b);
        }

        private static bool TryGetProperty(JsonElement el, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out value)) return true;
                var match = el.EnumerateObject().FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match.Value.ValueKind != JsonValueKind.Undefined)
                {
                    value = match.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static string? AttrOrChild(XElement el, params string[] names)
        {
            foreach (var name in names)
            {
                var attr = el.Attributes().FirstOrDefault(a =>
                    a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (attr != null && !string.IsNullOrWhiteSpace(attr.Value)) return attr.Value;
                var child = Child(el, name);
                if (child != null && !string.IsNullOrWhiteSpace(child.Value)
                    && !child.HasElements)
                    return child.Value;
            }
            return null;
        }

        private static XElement? Child(XElement el, params string[] names) =>
            el.Elements().FirstOrDefault(e =>
                names.Any(n => e.Name.LocalName.Equals(n, StringComparison.OrdinalIgnoreCase)));

        private sealed class StructuredPanelFile
        {
            public string? Vendor { get; set; }
            public string? Name { get; set; }
            public string? LotNumber { get; set; }
            public string? ExpirationDate { get; set; }
            public string? CatalogNumber { get; set; }
            public string? ProductLine { get; set; }
            public bool EnzymeTreated { get; set; }
            public List<string> AntigenOrder { get; } = new();
            public List<StructuredCell> Cells { get; } = new();
        }

        private sealed class StructuredCell
        {
            public string CellNumber { get; set; } = "";
            public string? DonorId { get; set; }
            public string? RhPhenotype { get; set; }
            public Dictionary<string, string> Antigens { get; } = new(StringComparer.Ordinal);
        }
    }
}
