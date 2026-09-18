using AntibodyPanels.Models;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AntibodyPanels.Tests;

public class VendorPanelImportTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "VendorPanels", name);

    [Fact]
    public void CsvImport_PersistsVendorMetadataAndCellNotes()
    {
        using var iso = new IsolatedDatabase();
        using var catalog = new VendorCatalogService();
        var parsed = catalog.ImportFile(VendorIds.Immucor, Fixture("vendor-generic.csv"));
        Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
        Assert.Equal(3, parsed.Cells.Count);
        Assert.Equal("+", parsed.Cells[0].GetAntigen("D"));
        Assert.Equal("10001", parsed.Cells[0].DonorId);
        Assert.Equal("R1R1", parsed.Cells[0].RhPhenotype);
        Assert.True(parsed.Cells[0].HasTypedAntigen("Doa"));

        parsed.Vendor = VendorIds.Immucor;
        parsed.LotNumber = "IMM-TEST-1";
        parsed.ExpirationDate = "2030-01-15";
        parsed.CatalogNumber = "0003032";
        parsed.ProductLine = "Panocell-10";
        parsed.SourceFormat = "csv";

        var id = new VendorPanelImportService(iso.Db).Persist(parsed);
        var stored = iso.Db.GetPanel(id);
        Assert.NotNull(stored);
        Assert.Equal(VendorIds.Immucor, stored!.Vendor);
        Assert.Equal("IMM-TEST-1", stored.LotNumber);
        Assert.Equal("0003032", stored.CatalogNumber);
        Assert.Equal("Panocell-10", stored.ProductLine);
        Assert.Equal("csv", stored.SourceFormat);
        Assert.False(string.IsNullOrWhiteSpace(stored.ImportedAt));

        var cells = iso.Db.GetPanelCells(id);
        var first = cells.First(c => c.CellNumber == "1");
        Assert.Equal("+", first.GetAntigen("D"));
        Assert.Equal("10001", first.DonorId);
        Assert.Equal("R1R1", first.RhPhenotype);
        Assert.Equal("-", first.GetAntigen("Doa"));
        Assert.Equal("+", cells.First(c => c.CellNumber == "3").GetAntigen("Doa"));
        Assert.Equal(stored.PanelId, iso.Db.FindPanelByVendorLot(VendorIds.Immucor, "IMM-TEST-1")!.PanelId);
        Assert.Contains("Fya", iso.Db.GetPanelDisplayAntigens(id));
        Assert.Contains("Doa", iso.Db.GetPanelDisplayAntigens(id));
    }

    [Fact]
    public void ImportedSubsetPanel_HidesAntigensNotOnSheet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_subset_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                "Cell,D,C,c,E,e,M,N,S,s\n" +
                "1,+,-,-,-,+,+,+,-,+\n" +
                "2,-,+,+,+,-,-,-,+,+\n");
            using var iso = new IsolatedDatabase();
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = "BIO-SUBSET-1";
            parsed.SourceFormat = "csv";
            parsed.AntigenOrder.Clear();
            parsed.AntigenOrder.AddRange(new[] { "D", "C", "c", "E", "e", "M", "N", "S", "s" });

            var id = new VendorPanelImportService(iso.Db).Persist(parsed);
            var shown = iso.Db.GetPanelDisplayAntigens(id);
            Assert.Equal(new[] { "D", "C", "c", "E", "e", "M", "N", "S", "s" }, shown);
            Assert.DoesNotContain("Fya", shown);
            Assert.DoesNotContain("Fyb", shown);
            Assert.DoesNotContain("K", shown);
            Assert.DoesNotContain("Jka", shown);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MedionCsvImport_PersistsVendorMetadata()
    {
        using var iso = new IsolatedDatabase();
        using var catalog = new VendorCatalogService();
        var parsed = catalog.ImportFile(VendorIds.Medion, Fixture("vendor-generic.csv"));
        Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
        Assert.Equal(3, parsed.Cells.Count);
        Assert.Equal("+", parsed.Cells[0].GetAntigen("D"));

        parsed.Vendor = VendorIds.Medion;
        parsed.LotNumber = "MED-TEST-1";
        parsed.ExpirationDate = "2030-06-01";
        parsed.CatalogNumber = "213654";
        parsed.ProductLine = "Data-Cyte Plus";
        parsed.SourceFormat = "csv";

        var id = new VendorPanelImportService(iso.Db).Persist(parsed);
        var stored = iso.Db.GetPanel(id);
        Assert.NotNull(stored);
        Assert.Equal(VendorIds.Medion, stored!.Vendor);
        Assert.Equal("MED-TEST-1", stored.LotNumber);
        Assert.Equal("213654", stored.CatalogNumber);
        Assert.Equal("Data-Cyte Plus", stored.ProductLine);
        Assert.Equal("csv", stored.SourceFormat);
        Assert.False(string.IsNullOrWhiteSpace(stored.ImportedAt));
        Assert.Equal(stored.PanelId, iso.Db.FindPanelByVendorLot(VendorIds.Medion, "MED-TEST-1")!.PanelId);
    }

    [Fact]
    public void DuplicateLot_ThrowsUnlessReplaceRequested()
    {
        using var iso = new IsolatedDatabase();
        using var catalog = new VendorCatalogService();
        var parsed = catalog.ImportFile(VendorIds.Grifols, Fixture("vendor-generic.csv"));
        parsed.Vendor = VendorIds.Grifols;
        parsed.LotNumber = "GRI-DUP";
        var importer = new VendorPanelImportService(iso.Db);
        importer.Persist(parsed);
        Assert.Throws<InvalidOperationException>(() => importer.Persist(parsed));
        var id = importer.Persist(parsed, replaceExisting: true);
        Assert.Equal("GRI-DUP", iso.Db.GetPanel(id)!.LotNumber);
        Assert.Single(iso.Db.GetAllPanels().Where(p => p.LotNumber == "GRI-DUP"));
    }

    [Fact]
    public void BioRadHtml_ListsLotsAndPdfUrl()
    {
        using var http = new VendorHttpClient();
        var source = new BioRadIhAreaSource(http);
        var lots = source.ParseIhAreaHtml(File.ReadAllText(Fixture("biorad-iharea.html")));
        Assert.Contains(lots, l => l.LotNumber == "45161.56.1" && l.ExpirationDate == "2026-11-09");
        Assert.Contains(lots, l => l.LotNumber == "45171.56.1" && l.EnzymeTreated);
        Assert.All(lots, l => Assert.Contains("backend.ih-area.bio-rad.com", l.DownloadUrl));
    }

    [Fact]
    public void OrthoHtml_ListsResolveLots()
    {
        using var http = new VendorHttpClient();
        var lots = new OrthoEAntigramSource(http).ParseQuickAntigramHtml(
            File.ReadAllText(Fixture("ortho-quick.html")));
        Assert.Contains(lots, l => l.LotNumber == "RA301" && l.ProductLine == "Resolve Panel A");
        Assert.Contains(lots, l => l.LotNumber == "RB715");
        Assert.Contains(lots, l => l.LotNumber == "RC438");
    }

    [Fact]
    public void QuotientHtml_ListsAlbacytePdf()
    {
        using var http = new VendorHttpClient();
        var lots = new QuotientAliveDxSource(http).ParseProductHtml(
            File.ReadAllText(Fixture("quotient-products.html")));
        Assert.Contains(lots, l => l.LotNumber == "V265842" && l.DownloadUrl!.Contains("V265842"));
    }

    [Fact]
    public void FileOnlyVendors_CannotListLots()
    {
        using var catalog = new VendorCatalogService();
        Assert.False(catalog.GetSource(VendorIds.Immucor).CanListLots);
        Assert.False(catalog.GetSource(VendorIds.Grifols).CanListLots);
        Assert.False(catalog.GetSource(VendorIds.Medion).CanListLots);
        Assert.True(catalog.GetSource(VendorIds.BioRad).CanListLots);
    }

    [Theory]
    [InlineData("Set ID-DiaPanel: 45161.31.x (Japan: 4516.31.xx) 2025.11.24", "45161.31.x")]
    [InlineData("Lot No: V265925 Expiry Date: 2023.12.04", "V265925")]
    [InlineData("ALBAcyte V265842_V265844.pdf", "V265842")]
    [InlineData("Resolve Panel A RA301 Exp 2026/10/13", "RA301")]
    public void ExtractLot_ReadsVendorSpecificLabels(string text, string expected)
    {
        Assert.Equal(expected, VendorAntigramParser.ExtractLot(text));
    }

    [Theory]
    [InlineData("Expiry Date: 2023.12.04", "2023-12-04")]
    [InlineData("Exp. date: 2026.04.13", "2026-04-13")]
    [InlineData("Set ID-DiaPanel: 45161.31.x 2025.11.24", "2025-11-24")]
    public void ExtractExpiration_NormalizesVendorDates(string text, string expected)
    {
        Assert.Equal(expected, VendorAntigramParser.ExtractExpiration(text));
    }

    [Fact]
    public void AntigenAliases_MapVendorHeadersAndValues()
    {
        Assert.Equal("Fya", VendorAntigenAliases.Resolve("Fy^a"));
        Assert.Equal("Kpa", VendorAntigenAliases.Resolve("Kp a"));
        Assert.Equal("C", VendorAntigenAliases.Resolve("C"));
        Assert.Equal("c", VendorAntigenAliases.Resolve("c"));
        Assert.Equal("E", VendorAntigenAliases.Resolve("E"));
        Assert.Equal("e", VendorAntigenAliases.Resolve("e"));
        Assert.Equal("K", VendorAntigenAliases.Resolve("K"));
        Assert.Equal("k", VendorAntigenAliases.Resolve("k"));
        Assert.Equal("S", VendorAntigenAliases.Resolve("S"));
        Assert.Equal("s", VendorAntigenAliases.Resolve("s"));
        Assert.Equal("+", VendorAntigenAliases.NormalizeValue("+w", true));
        Assert.Equal("-", VendorAntigenAliases.NormalizeValue("0", true));
        Assert.Null(VendorAntigenAliases.NormalizeValue("NT", required: false));
    }

    [Fact]
    public void PdfParser_ReadsPositionedAntigenGrid()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"vendor_{Guid.NewGuid():N}.pdf");
        try
        {
            WriteFixturePdf(pdfPath);
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, pdfPath);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Equal(2, parsed.Cells.Count);
            var cell1 = parsed.Cells.First(c => c.CellNumber == "1");
            Assert.Equal("+", cell1.GetAntigen("D"));
            Assert.Equal("+", cell1.GetAntigen("C"));
            Assert.Equal("-", cell1.GetAntigen("c"));
            var cell2 = parsed.Cells.First(c => c.CellNumber == "2");
            Assert.Equal("-", cell2.GetAntigen("D"));
            Assert.Equal("45161.99.1", parsed.LotNumber);
            Assert.Equal("2026-12-01", parsed.ExpirationDate);
            Assert.Contains("Fya", parsed.AntigenOrder);
            Assert.DoesNotContain("Lua", parsed.AntigenOrder);
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    [Fact]
    public void PdfParser_JoinsSplitSuperscriptHeaders()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"vendor_split_{Guid.NewGuid():N}.pdf");
        try
        {
            WriteSplitHeaderPdf(pdfPath);
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, pdfPath);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            Assert.Contains("Fya", parsed.AntigenOrder);
            Assert.Contains("Fyb", parsed.AntigenOrder);
            Assert.Contains("Cw", parsed.AntigenOrder);
            var cell1 = parsed.Cells.First(c => c.CellNumber == "1");
            Assert.Equal("+", cell1.GetAntigen("Fya"));
            Assert.Equal("-", cell1.GetAntigen("Fyb"));
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    [Fact]
    public void PanelFilter_IncludesCatalogAndProductLine()
    {
        var p = new Panel
        {
            Name = "ID-DiaPanel",
            LotNumber = "45161.56.1",
            Vendor = "Bio-Rad",
            CatalogNumber = "45161",
            ProductLine = "ID-DiaPanel"
        };
        Assert.True(p.MatchesFilter("45161"));
        Assert.True(p.MatchesFilter("DiaPanel"));
        Assert.False(p.MatchesFilter("Panocell"));
    }

    private static void WriteFixturePdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = 800;
        page.Height = 400;
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);
        gfx.DrawString("ID-DiaPanel LOT 45161.99.1 Exp. date: 2026.12.01", font, XBrushes.Black, 20, 30);
        string[] headers = { "D", "C", "c", "E", "e", "K", "k", "Fya", "Fyb", "Jka", "Jkb" };
        for (int i = 0; i < headers.Length; i++)
            gfx.DrawString(headers[i], font, XBrushes.Black, 80 + i * 40, 80);
        gfx.DrawString("1", font, XBrushes.Black, 20, 110);
        string[] row1 = { "+", "+", "-", "-", "+", "-", "+", "+", "-", "+", "-" };
        for (int i = 0; i < row1.Length; i++)
            gfx.DrawString(row1[i], font, XBrushes.Black, 80 + i * 40, 110);
        gfx.DrawString("2", font, XBrushes.Black, 20, 140);
        string[] row2 = { "-", "-", "+", "-", "+", "+", "+", "-", "+", "-", "+" };
        for (int i = 0; i < row2.Length; i++)
            gfx.DrawString(row2[i], font, XBrushes.Black, 80 + i * 40, 140);
        doc.Save(path);
    }

    private static void WriteSplitHeaderPdf(string path)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = 700;
        page.Height = 300;
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);
        gfx.DrawString("ID-DiaPanel LOT 45161.88.1 Exp. date: 2026.12.01", font, XBrushes.Black, 20, 24);
        string[] stems = { "D", "C", "c", "E", "e", "C", "Fy", "Fy", "M", "N", "S", "s" };
        string[] mods  = { "",  "",  "",  "",  "",  "w", "a",  "b",  "",  "",  "",  "" };
        for (int i = 0; i < stems.Length; i++)
        {
            var x = 70 + i * 42;
            gfx.DrawString(stems[i], font, XBrushes.Black, x, 70);
            if (mods[i].Length > 0)
                gfx.DrawString(mods[i], font, XBrushes.Black, x + 8, 58);
        }
        gfx.DrawString("1", font, XBrushes.Black, 20, 110);
        string[] row1 = { "+", "+", "-", "-", "+", "-", "+", "-", "+", "+", "-", "+" };
        for (int i = 0; i < row1.Length; i++)
            gfx.DrawString(row1[i], font, XBrushes.Black, 70 + i * 42, 110);
        gfx.DrawString("2", font, XBrushes.Black, 20, 140);
        string[] row2 = { "-", "-", "+", "+", "+", "+", "-", "+", "-", "-", "+", "-" };
        for (int i = 0; i < row2.Length; i++)
            gfx.DrawString(row2[i], font, XBrushes.Black, 70 + i * 42, 140);
        doc.Save(path);
    }
}
