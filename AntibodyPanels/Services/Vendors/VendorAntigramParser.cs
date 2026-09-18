using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AntibodyPanels.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AntibodyPanels.Services.Vendors
{
    public static class VendorAntigramParser
    {
        private static readonly Regex LotRegex = new(
            @"\b(?:LOT|Lot|lot)\s*[:#]?\s*([A-Z0-9][A-Z0-9.\-/]{2,})",
            RegexOptions.Compiled);
        private static readonly Regex ExpRegex = new(
            @"\b(?:Exp(?:\.|iration)?|Vencimento|Valid(?:ity)?)\s*(?:date)?\s*[:.]?\s*(\d{4}[./-]\d{1,2}[./-]\d{1,2}|\d{1,2}[./-]\d{1,2}[./-]\d{2,4})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static VendorParseResult Parse(Stream stream, string vendor, string? fileNameHint,
            VendorLotListing? listing)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            var name = fileNameHint ?? listing?.LotNumber ?? "vendor-panel";
            if (LooksLikePdf(bytes, name))
                return ParsePdf(bytes, vendor, name, listing);
            return ParseCsv(bytes, vendor, name, listing);
        }

        public static VendorParseResult ParseCsv(byte[] bytes, string vendor, string fileName,
            VendorLotListing? listing)
        {
            var text = DecodeText(bytes);
            using var reader = new StringReader(text);
            var rows = new List<string[]>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                rows.Add(SplitCsvLine(line));
            }
            if (rows.Count < 2)
            {
                return Fail(vendor, listing, fileName, "CSV has no data rows.");
            }

            var header = rows[0];
            var cellIdx = FindCellColumn(header);
            if (cellIdx < 0)
                return Fail(vendor, listing, fileName, "CSV must include a Cell, Cell #, or Donor column.");

            var antigenCols = new List<(int Index, string Antigen)>();
            var order = new List<string>();
            for (int i = 0; i < header.Length; i++)
            {
                if (i == cellIdx) continue;
                var ag = VendorAntigenAliases.Resolve(header[i]);
                if (ag == null || antigenCols.Any(c => c.Antigen == ag)) continue;
                antigenCols.Add((i, ag));
                order.Add(ag);
            }
            if (antigenCols.Count == 0)
                return Fail(vendor, listing, fileName, "CSV has no recognized antigen columns.");

            var result = SeedResult(vendor, listing, fileName, text);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (cellIdx >= row.Length) continue;
                var cellNumber = row[cellIdx].Trim();
                if (string.IsNullOrWhiteSpace(cellNumber)) continue;
                if (!seen.Add(cellNumber))
                {
                    result.Errors.Add($"Duplicate cell '{cellNumber}'.");
                    continue;
                }
                var cell = new PanelCell { CellNumber = NormalizeCellNumber(cellNumber) };
                TryFillDonor(cell, header, row);
                foreach (var (idx, ag) in antigenCols)
                {
                    var raw = idx < row.Length ? row[idx] : "";
                    var required = AntigenConstants.IsStandard(ag);
                    var value = VendorAntigenAliases.NormalizeValue(raw, required);
                    if (value == null)
                    {
                        if (required)
                            result.Errors.Add($"Cell {cell.CellNumber}: invalid value '{raw}' for {ag}.");
                        continue;
                    }
                    cell.SetAntigen(ag, value);
                }
                result.Cells.Add(cell);
            }
            result.AntigenOrder.AddRange(order);
            if (result.Cells.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add("CSV contains no panel cells.");
            return result;
        }

        public static VendorParseResult ParsePdf(byte[] bytes, string vendor, string fileName,
            VendorLotListing? listing)
        {
            string fullText;
            List<PdfWord> words;
            try
            {
                using var doc = PdfDocument.Open(bytes);
                words = ExtractWords(doc);
                fullText = string.Join("\n", doc.GetPages().Select(p => p.Text));
            }
            catch (Exception ex)
            {
                return Fail(vendor, listing, fileName, "Could not read PDF: " + ex.Message);
            }

            var result = SeedResult(vendor, listing, fileName, fullText);
            var rows = ClusterRows(words);
            var header = rows
                .Select((row, idx) => new { row, idx, hits = CountAntigenHits(row) })
                .Where(x => x.hits >= 6)
                .OrderByDescending(x => x.hits)
                .FirstOrDefault();
            if (header == null)
            {
                result.Errors.Add("PDF does not contain a recognizable antigen header row.");
                return result;
            }

            var columns = BuildAntigenColumns(header.row);
            if (columns.Count < 6)
            {
                result.Errors.Add("PDF antigen header could not be mapped.");
                return result;
            }

            result.AntigenOrder.AddRange(columns.Select(c => c.Antigen).Distinct());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Skip(header.idx + 1))
            {
                var cellNumber = GuessCellNumber(row);
                if (cellNumber == null) continue;
                if (!seen.Add(cellNumber)) continue;

                var cell = new PanelCell { CellNumber = cellNumber };
                cell.RhPhenotype = GuessRhPhenotype(row);
                cell.DonorId = GuessDonorId(row, cellNumber);
                var leftovers = new List<string>();
                foreach (var word in row)
                {
                    if (IsCellToken(word.Text, cellNumber)) continue;
                    var nearest = columns.OrderBy(c => Math.Abs(c.X - word.X)).First();
                    if (Math.Abs(nearest.X - word.X) > 18) continue;
                    var required = AntigenConstants.IsStandard(nearest.Antigen);
                    var value = VendorAntigenAliases.NormalizeValue(word.Text, required);
                    if (value == null)
                    {
                        if (LooksLikeNote(word.Text))
                            leftovers.Add(word.Text);
                        continue;
                    }
                    if (!cell.HasTypedAntigen(nearest.Antigen))
                        cell.SetAntigen(nearest.Antigen, value);
                }
                foreach (var ag in AntigenConstants.Antigens)
                {
                    if (!cell.HasTypedAntigen(ag))
                        cell.SetAntigen(ag, "-");
                }
                if (leftovers.Count > 0)
                    cell.SpecialTypes = string.Join(" ", leftovers);
                result.Cells.Add(cell);
            }

            if (result.Cells.Count == 0)
                result.Errors.Add("PDF antigen table had a header but no cell rows.");
            return result;
        }

        private static VendorParseResult SeedResult(string vendor, VendorLotListing? listing,
            string fileName, string text)
        {
            var lot = listing?.LotNumber ?? ExtractLot(text);
            var exp = listing?.ExpirationDate ?? ExtractExpiration(text);
            var product = listing?.ProductLine
                ?? GuessProductLine(vendor, text, fileName);
            return new VendorParseResult
            {
                Vendor = vendor,
                Name = BuildName(vendor, product, lot),
                LotNumber = lot,
                ExpirationDate = exp,
                CatalogNumber = listing?.CatalogNumber ?? ExtractCatalog(vendor, text),
                ProductLine = product,
                EnzymeTreated = listing?.EnzymeTreated == true ||
                    text.Contains("papain", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("ficin", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("-P", StringComparison.OrdinalIgnoreCase),
                SourceUrl = listing?.DownloadUrl,
                SourceFormat = listing?.SourceFormat ?? GuessFormat(fileName),
                SpecialNotes = listing?.Notes
            };
        }

        private static VendorParseResult Fail(string vendor, VendorLotListing? listing,
            string fileName, string error)
        {
            var result = SeedResult(vendor, listing, fileName, fileName);
            result.Errors.Add(error);
            return result;
        }

        private static string BuildName(string vendor, string? product, string? lot)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(product)) parts.Add(product.Trim());
            else parts.Add(vendor + " panel");
            if (!string.IsNullOrWhiteSpace(lot)) parts.Add(lot.Trim());
            return string.Join(" ", parts);
        }

        private static string GuessFormat(string fileName) =>
            fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "pdf";

        private static bool LooksLikePdf(byte[] bytes, string fileName)
        {
            if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return true;
            return bytes.Length >= 5 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P'
                && bytes[2] == (byte)'D' && bytes[3] == (byte)'F';
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string[] SplitCsvLine(string line)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                    continue;
                }
                if (ch == ',' && !quoted)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            list.Add(sb.ToString());
            return list.ToArray();
        }

        private static int FindCellColumn(string[] header)
        {
            for (int i = 0; i < header.Length; i++)
            {
                var key = VendorAntigenAliases.NormalizeKey(header[i]);
                if (key is "CELL" or "CELLNUMBER" or "CELLNO" or "NO" or "NUMBER"
                    or "DONOR" or "DONORID" or "DONORNO" or "VIAL")
                    return i;
            }
            return -1;
        }

        private static void TryFillDonor(PanelCell cell, string[] header, string[] row)
        {
            for (int i = 0; i < header.Length && i < row.Length; i++)
            {
                var key = VendorAntigenAliases.NormalizeKey(header[i]);
                if (key is "DONOR" or "DONORID" or "DONORNO")
                    cell.DonorId = row[i].Trim();
                else if (key is "RH" or "RHHR" or "PHENOTYPE" or "RHPHENOTYPE")
                    cell.RhPhenotype = row[i].Trim();
                else if (key is "SPECIALTYPES" or "NOTES" or "SPECIAL")
                    cell.SpecialTypes = row[i].Trim();
            }
        }

        private static string NormalizeCellNumber(string raw)
        {
            if (string.Equals(raw, "AC", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("auto", StringComparison.OrdinalIgnoreCase))
                return "AC";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? digits.TrimStart('0').PadLeft(1, '0') : raw.Trim();
        }

        private sealed class PdfWord
        {
            public string Text { get; init; } = "";
            public double X { get; init; }
            public double Y { get; init; }
        }

        private sealed class AntigenColumn
        {
            public string Antigen { get; init; } = "";
            public double X { get; init; }
        }

        private static List<PdfWord> ExtractWords(PdfDocument doc)
        {
            var list = new List<PdfWord>();
            foreach (var page in doc.GetPages())
            {
                foreach (var word in page.GetWords())
                {
                    var text = word.Text.Trim();
                    if (text.Length == 0) continue;
                    list.Add(new PdfWord
                    {
                        Text = text,
                        X = word.BoundingBox.Left,
                        Y = Math.Round(word.BoundingBox.Bottom, 1)
                    });
                }
            }
            return list;
        }

        private static List<List<PdfWord>> ClusterRows(List<PdfWord> words)
        {
            var rows = new List<List<PdfWord>>();
            foreach (var word in words.OrderByDescending(w => w.Y).ThenBy(w => w.X))
            {
                var row = rows.LastOrDefault();
                if (row == null || Math.Abs(row[0].Y - word.Y) > 3.5)
                {
                    row = new List<PdfWord>();
                    rows.Add(row);
                }
                row.Add(word);
            }
            return rows;
        }

        private static int CountAntigenHits(List<PdfWord> row) =>
            row.Select(w => VendorAntigenAliases.Resolve(w.Text)).Count(a => a != null);

        private static List<AntigenColumn> BuildAntigenColumns(List<PdfWord> header)
        {
            var cols = new List<AntigenColumn>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in header.OrderBy(w => w.X))
            {
                var ag = VendorAntigenAliases.Resolve(word.Text);
                if (ag == null || !seen.Add(ag)) continue;
                cols.Add(new AntigenColumn { Antigen = ag, X = word.X });
            }
            return cols;
        }

        private static string? GuessCellNumber(List<PdfWord> row)
        {
            foreach (var word in row.OrderBy(w => w.X))
            {
                var t = word.Text.Trim();
                if (Regex.IsMatch(t, @"^(?:AC|A\.C\.)$", RegexOptions.IgnoreCase))
                    return "AC";
                if (Regex.IsMatch(t, @"^\d{1,2}$"))
                {
                    var n = int.Parse(t, CultureInfo.InvariantCulture);
                    if (n is >= 1 and <= 40) return n.ToString(CultureInfo.InvariantCulture);
                }
                if (Regex.IsMatch(t, @"^\d{1,2}[A-Za-z]?$") && char.IsDigit(t[0]))
                {
                    var digits = new string(t.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var n) && n is >= 1 and <= 40)
                        return n.ToString(CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        private static bool IsCellToken(string text, string cellNumber) =>
            string.Equals(NormalizeCellNumber(text), cellNumber, StringComparison.OrdinalIgnoreCase);

        private static string? GuessRhPhenotype(List<PdfWord> row)
        {
            foreach (var word in row)
            {
                var t = word.Text.Trim();
                if (Regex.IsMatch(t, @"^(R[012]R[012]|R[012]r|r[''″]?r|rr)$", RegexOptions.IgnoreCase))
                    return t;
            }
            return null;
        }

        private static string? GuessDonorId(List<PdfWord> row, string cellNumber)
        {
            foreach (var word in row.OrderBy(w => w.X))
            {
                var t = word.Text.Trim();
                if (t == cellNumber) continue;
                if (Regex.IsMatch(t, @"^\d{5,}$"))
                    return t;
            }
            return null;
        }

        private static bool LooksLikeNote(string text) =>
            text.Length > 2 && !Regex.IsMatch(text, @"^[+\-0]$");

        public static string? ExtractLot(string text)
        {
            var m = LotRegex.Match(text);
            return m.Success ? m.Groups[1].Value.Trim().TrimEnd('.') : null;
        }

        public static string? ExtractExpiration(string text)
        {
            var m = ExpRegex.Match(text);
            return m.Success ? NormalizeDate(m.Groups[1].Value) : null;
        }

        public static string? NormalizeDate(string raw)
        {
            var parts = raw.Split('.', '/', '-');
            if (parts.Length != 3) return raw;
            if (parts[0].Length == 4 &&
                int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var d))
                return $"{parts[0]}-{m:00}-{d:00}";
            if (parts[2].Length >= 2 &&
                int.TryParse(parts[0], out var d2) && int.TryParse(parts[1], out var m2))
            {
                var y = parts[2].Length == 2 ? "20" + parts[2] : parts[2];
                return $"{y}-{m2:00}-{d2:00}";
            }
            return raw;
        }

        private static string? ExtractCatalog(string vendor, string text)
        {
            if (vendor == VendorIds.BioRad)
            {
                var m = Regex.Match(text, @"\b(45161|45171|45241)\b");
                return m.Success ? m.Value : null;
            }
            if (vendor == VendorIds.Quotient)
            {
                var m = Regex.Match(text, @"\b(Z47[123]U?)\b", RegexOptions.IgnoreCase);
                return m.Success ? m.Value.ToUpperInvariant() : null;
            }
            return null;
        }

        private static string? GuessProductLine(string vendor, string text, string fileName)
        {
            var hay = text + " " + fileName;
            if (hay.Contains("ID-DiaPanel-P", StringComparison.OrdinalIgnoreCase)) return "ID-DiaPanel-P";
            if (hay.Contains("ID-DiaPanel", StringComparison.OrdinalIgnoreCase)) return "ID-DiaPanel";
            if (hay.Contains("Resolve Panel A", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(hay, @"\bRA\d{3}\b")) return "Resolve Panel A";
            if (hay.Contains("Resolve Panel B", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(hay, @"\bRB\d{3}\b")) return "Resolve Panel B";
            if (hay.Contains("Resolve Panel C", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(hay, @"\bRC\d{3}\b")) return "Resolve Panel C";
            if (hay.Contains("Panocell", StringComparison.OrdinalIgnoreCase)) return "Panocell";
            if (hay.Contains("Data-Cyte", StringComparison.OrdinalIgnoreCase)) return "Data-Cyte Plus";
            if (hay.Contains("Identisera", StringComparison.OrdinalIgnoreCase)) return "Identisera Diana";
            if (hay.Contains("ALBAcyte", StringComparison.OrdinalIgnoreCase)) return "ALBAcyte Antibody ID";
            return vendor switch
            {
                VendorIds.BioRad => "ID-DiaPanel",
                VendorIds.Ortho => "Resolve Panel",
                VendorIds.Immucor => "Panocell",
                VendorIds.Grifols => "Data-Cyte Plus",
                VendorIds.Medion => "Data-Cyte Plus",
                VendorIds.Quotient => "ALBAcyte Antibody ID",
                _ => null
            };
        }
    }
}
