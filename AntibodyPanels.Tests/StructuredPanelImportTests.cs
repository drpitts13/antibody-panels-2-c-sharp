using System.IO.Compression;
using System.Text;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class StructuredPanelImportTests
{
    private const string JsonPanel = """
        {
          "vendor": "Immucor",
          "name": "Panocell structured",
          "lotNumber": "JSON-LOT-1",
          "expirationDate": "2030-01-15",
          "catalogNumber": "0003032",
          "productLine": "Panocell-10",
          "antigenOrder": ["D", "C", "c", "E", "e", "K"],
          "cells": [
            {
              "cellNumber": "1",
              "donorId": "10001",
              "rhPhenotype": "R1R1",
              "antigens": { "D": "+", "C": "+", "c": "-", "E": "-", "e": "+", "K": "-" }
            },
            {
              "cellNumber": "2",
              "donorId": "10002",
              "antigens": { "D": "-", "C": "-", "c": "+", "E": "+", "e": "-", "K": "+" }
            }
          ]
        }
        """;

    private const string XmlPanel = """
        <panel vendor="Grifols" name="Screen structured" lotNumber="XML-LOT-1"
               expirationDate="2031-06-01" catalogNumber="213654" productLine="Screen">
          <antigenOrder>
            <antigen>D</antigen><antigen>C</antigen><antigen>c</antigen>
            <antigen>E</antigen><antigen>e</antigen>
          </antigenOrder>
          <cells>
            <cell cellNumber="1" donorId="G1" rhPhenotype="R1R1">
              <antigen name="D">+</antigen>
              <antigen name="C">+</antigen>
              <antigen name="c">-</antigen>
              <antigen name="E">-</antigen>
              <antigen name="e">+</antigen>
            </cell>
            <cell cellNumber="2">
              <antigen name="D">-</antigen>
              <antigen name="C">-</antigen>
              <antigen name="c">+</antigen>
              <antigen name="E">+</antigen>
              <antigen name="e">-</antigen>
            </cell>
          </cells>
        </panel>
        """;

    [Fact]
    public void JsonImport_TypesOnlyListedAntigens_AndStaysInactive()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteTemp(".json", JsonPanel);
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.Immucor, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Equal(2, parsed.Cells.Count);
            Assert.Equal("json", parsed.SourceFormat);
            Assert.Equal("JSON-LOT-1", parsed.LotNumber);
            Assert.Equal("+", parsed.Cells[0].GetAntigen("D"));
            Assert.Equal("10001", parsed.Cells[0].DonorId);
            Assert.Equal("R1R1", parsed.Cells[0].RhPhenotype);
            Assert.False(parsed.Cells[0].HasTypedAntigen("Fya"));
            Assert.False(parsed.Cells[0].HasTypedAntigen("Jka"));

            var outcome = new VendorPanelImportService(iso.Db).Import(parsed);
            var stored = iso.Db.GetPanel(outcome.PanelId);
            Assert.False(stored!.IsActive);
            Assert.Equal("json", stored.SourceFormat);
            Assert.Equal("JSON-LOT-1", stored.LotNumber);
            Assert.True(File.Exists(stored.SourceArtifactPath));
            Assert.Equal(parsed.SourceBytes, File.ReadAllBytes(stored.SourceArtifactPath!));
            Assert.Contains("stored inactive pending review", outcome.Review.Explanation);

            var cells = iso.Db.GetPanelCells(outcome.PanelId);
            Assert.False(cells[0].HasTypedAntigen("Fya"));
            Assert.True(cells[0].HasTypedAntigen("K"));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void XmlImport_TypesOnlyListedAntigens()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteTemp(".xml", XmlPanel);
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.Grifols, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Equal("xml", parsed.SourceFormat);
            Assert.Equal("XML-LOT-1", parsed.LotNumber);
            Assert.Equal(2, parsed.Cells.Count);
            Assert.False(parsed.Cells[0].HasTypedAntigen("K"));
            Assert.Equal("+", parsed.Cells[0].GetAntigen("D"));

            var id = new VendorPanelImportService(iso.Db).Persist(parsed);
            var stored = iso.Db.GetPanel(id);
            Assert.False(stored!.IsActive);
            Assert.Equal("xml", stored.SourceFormat);
            Assert.DoesNotContain("K", iso.Db.GetPanelTypedAntigens(id));
            Assert.Contains("E", iso.Db.GetPanelTypedAntigens(id));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void XlsxImport_ReadsFirstSheetLikeCsv()
    {
        using var iso = new IsolatedDatabase();
        var path = Path.Combine(Path.GetTempPath(), $"panel_{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, BuildXlsx(new[]
        {
            new[] { "Cell", "D", "C", "c", "E", "e", "Donor" },
            new[] { "1", "+", "+", "-", "-", "+", "X1" },
            new[] { "2", "-", "-", "+", "+", "-", "X2" },
        }));
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Equal("xlsx", parsed.SourceFormat);
            Assert.Equal(2, parsed.Cells.Count);
            Assert.Equal("+", parsed.Cells[0].GetAntigen("D"));
            Assert.Equal("X1", parsed.Cells[0].DonorId);
            Assert.False(parsed.Cells[0].HasTypedAntigen("K"));
            parsed.LotNumber = "XLSX-LOT-1";

            var id = new VendorPanelImportService(iso.Db).Persist(parsed);
            Assert.False(iso.Db.GetPanel(id)!.IsActive);
            Assert.Equal("xlsx", iso.Db.GetPanel(id)!.SourceFormat);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LabJsonImport_PersistsInactiveArtifact_WithoutInventedNegatives()
    {
        using var iso = new IsolatedDatabase();
        var path = WriteTemp(".json", JsonPanel);
        try
        {
            var parsed = PanelCsvService.Import(path);
            Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
            Assert.False(parsed.Cells[0].Antigens.ContainsKey("Fya"));
            Assert.Equal("+", parsed.Cells[0].Antigens["D"]);

            var outcome = new LabPanelImportService(iso.Db).Import(parsed, new LabPanelImportRequest
            {
                Name = "Lab JSON",
                LotNumber = "LAB-JSON-1",
                Vendor = "Local lab",
                NumCells = 2,
                StartCell = 1
            });
            var stored = iso.Db.GetPanel(outcome.PanelId);
            Assert.False(stored!.IsActive);
            Assert.Equal("json", stored.SourceFormat);
            Assert.True(File.Exists(stored.SourceArtifactPath));
            Assert.False(iso.Db.GetPanelCells(outcome.PanelId)[0].HasTypedAntigen("Fya"));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void InvalidJson_ReportsParseError()
    {
        var path = WriteTemp(".json", "{ not json");
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.Immucor, path);
            Assert.False(parsed.Success);
            Assert.Contains(parsed.Errors, e => e.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void HeterozygousConflictingJson_DoesNotTypeBlankAntigens()
    {
        var json = """
            {
              "lotNumber": "BLANK-1",
              "cells": [
                { "cellNumber": "1", "antigens": { "E": "+", "e": "NT" } }
              ]
            }
            """;
        var path = WriteTemp(".json", json);
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.Ortho, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.True(parsed.Cells[0].HasTypedAntigen("E"));
            Assert.False(parsed.Cells[0].HasTypedAntigen("e"));
            Assert.False(parsed.Cells[0].HasTypedAntigen("K"));
        }
        finally { TryDelete(path); }
    }

    private static string WriteTemp(string ext, string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"panel_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, text);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    internal static byte[] BuildXlsx(IReadOnlyList<string[]> rows)
    {
        var strings = new List<string>();
        string Share(string value)
        {
            var idx = strings.IndexOf(value);
            if (idx >= 0) return idx.ToString();
            strings.Add(value);
            return (strings.Count - 1).ToString();
        }

        var sheet = new StringBuilder();
        sheet.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var refId = $"{(char)('A' + c)}{r + 1}";
                var idx = Share(rows[r][c]);
                sheet.Append($"<c r=\"{refId}\" t=\"s\"><v>{idx}</v></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var sst = new StringBuilder();
        sst.Append($"""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{strings.Count}" uniqueCount="{strings.Count}">""");
        foreach (var s in strings)
            sst.Append($"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>");
        sst.Append("</sst>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
                </Types>
                """);
            WriteEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(zip, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
                </Relationships>
                """);
            WriteEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            WriteEntry(zip, "xl/sharedStrings.xml", sst.ToString());
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
