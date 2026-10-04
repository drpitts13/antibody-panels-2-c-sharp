using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class PanelImportReviewTests
{
    [Fact]
    public void CompareSchema_ReportsAddedAndRemovedAntigens()
    {
        var diff = PanelImportReviewer.CompareSchema(
            new[] { "D", "C", "c", "E", "e", "Lua" },
            new[] { "D", "C", "c", "E", "e", "Kpa" });

        Assert.True(diff.Changed);
        Assert.Contains("Lua", diff.Added);
        Assert.Contains("Kpa", diff.Removed);
        Assert.DoesNotContain("E", diff.Added);
        Assert.DoesNotContain("E", diff.Removed);
    }

    [Fact]
    public void Hash_IsStableForSameBytes()
    {
        var bytes = "Cell,D,C\n1,+,-"u8.ToArray();
        Assert.Equal(PanelImportReviewer.Hash(bytes), PanelImportReviewer.Hash(bytes));
        Assert.NotEqual(PanelImportReviewer.Hash(bytes), PanelImportReviewer.Hash("other"u8.ToArray()));
    }

    [Fact]
    public void Import_StoresInactiveWithArtifactAndNoPriorLotSentence()
    {
        using var iso = new IsolatedDatabase();
        var parsed = ParseCsv("""
            Cell,D,C,c,E,e
            1,+,-,-,-,+
            2,-,+,+,+,-
            """, "LOT-A", "10-cell");

        var outcome = new VendorPanelImportService(iso.Db).Import(parsed);
        var stored = iso.Db.GetPanel(outcome.PanelId);

        Assert.False(stored!.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(stored.SourceSha256));
        Assert.Equal(outcome.Review.Sha256, stored.SourceSha256);
        Assert.True(File.Exists(stored.SourceArtifactPath));
        Assert.Equal(parsed.SourceBytes, File.ReadAllBytes(stored.SourceArtifactPath!));
        Assert.Equal(PanelImportReviewer.Hash(parsed.SourceBytes!), stored.SourceSha256);
        Assert.Contains("stored inactive pending review", outcome.Review.Explanation);
        Assert.Contains("No prior lot", outcome.Review.Explanation);
        Assert.Contains(stored.SourceSha256, outcome.Review.Explanation);
        Assert.DoesNotContain(iso.Db.GetActivePanels(), p => p.PanelId == outcome.PanelId);
    }

    [Fact]
    public void Import_SecondLot_ExplainsAddedAndRemovedAntigens()
    {
        using var iso = new IsolatedDatabase();
        var first = ParseCsv("""
            Cell,D,C,c,E,e,Kpa
            1,+,-,-,-,+,+
            2,-,+,+,+,-,-
            """, "LOT-1", "10-cell");
        var firstId = new VendorPanelImportService(iso.Db).Persist(first);

        var second = ParseCsv("""
            Cell,D,C,c,E,e,Lua
            1,+,-,-,-,+,-
            2,-,+,+,+,-,+
            """, "LOT-2", "10-cell");
        var outcome = new VendorPanelImportService(iso.Db).Import(second);

        Assert.Equal("LOT-1", outcome.Review.PriorLotNumber);
        Assert.Equal(firstId, outcome.Review.PriorPanelId);
        Assert.Contains("Lua", outcome.Review.Schema.Added);
        Assert.Contains("Kpa", outcome.Review.Schema.Removed);
        Assert.Contains("differs from prior lot LOT-1", outcome.Review.Explanation);
        Assert.Contains("Added: Lua", outcome.Review.Explanation);
        Assert.Contains("Removed: Kpa", outcome.Review.Explanation);
        Assert.False(iso.Db.GetPanel(outcome.PanelId)!.IsActive);
    }

    [Fact]
    public void Import_MatchingSchema_SaysMatchesPriorLot()
    {
        using var iso = new IsolatedDatabase();
        var csv = """
            Cell,D,C,c,E,e
            1,+,-,-,-,+
            2,-,+,+,+,-
            """;
        new VendorPanelImportService(iso.Db).Persist(ParseCsv(csv, "LOT-1", "10-cell"));
        var outcome = new VendorPanelImportService(iso.Db).Import(ParseCsv(csv, "LOT-2", "10-cell"));

        Assert.False(outcome.Review.Schema.Changed);
        Assert.Equal("LOT-1", outcome.Review.PriorLotNumber);
        Assert.Contains("matches prior lot LOT-1", outcome.Review.Explanation);
    }

    [Fact]
    public void Inspector_FlagsHomozygousConflict_NotHeterozygousOrUntypedPartner()
    {
        var conflict = new PanelCell { CellNumber = "1" };
        conflict.SetAntigen("E", "++");
        conflict.SetAntigen("e", "+");
        var het = new PanelCell { CellNumber = "2" };
        het.SetAntigen("E", "+");
        het.SetAntigen("e", "+");
        var homo = new PanelCell { CellNumber = "3" };
        homo.SetAntigen("E", "++");
        homo.SetAntigen("e", "-");
        var untyped = new PanelCell { CellNumber = "4" };
        untyped.SetAntigen("E", "++");

        var issues = PanelTypingInspector.Inspect(new[] { conflict, het, homo, untyped });
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.ImpossibleValue
            && i.CellNumber == "1" && i.Explanation.Contains("E") && i.Explanation.Contains("e"));
        Assert.DoesNotContain(issues, i => i.CellNumber is "2" or "3" or "4");
    }

    [Fact]
    public void Inspector_HighlightsUnknownAntigens_AndInvalidValues()
    {
        var cell = new PanelCell { CellNumber = "7" };
        cell.SetAntigen("K", "5+");
        var issues = PanelTypingInspector.Inspect(new[] { cell }, new[] { "Fy3", "Donor", "K" });
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.UnknownAntigen
            && i.Explanation.Contains("Fy3") && !i.Explanation.Contains("Donor"));
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.InvalidValue
            && i.CellNumber == "7" && i.Explanation.Contains("5+"));
    }

    [Fact]
    public void Import_HomozygousConflictAndUnknownColumn_StayInactiveWithReview()
    {
        using var iso = new IsolatedDatabase();
        var parsed = ParseCsv("""
            Cell,D,C,c,E,e,Fy3
            1,+,-,-,++,+,+
            2,-,+,+,+,-,-
            """, "LOT-CONFLICT", "10-cell");

        Assert.Contains("Fy3", parsed.UnknownAntigens);
        var outcome = new VendorPanelImportService(iso.Db).Import(parsed);
        Assert.False(iso.Db.GetPanel(outcome.PanelId)!.IsActive);
        Assert.Contains(outcome.Review.TypingIssues, i => i.Kind == PanelTypingInspector.ImpossibleValue);
        Assert.Contains(outcome.Review.TypingIssues, i => i.Kind == PanelTypingInspector.UnknownAntigen);
        Assert.Contains("marks E homozygous", outcome.Review.Explanation);
        Assert.Contains("Unknown antigen columns were not imported: Fy3", outcome.Review.Explanation);
        Assert.Contains("not a diagnosis", outcome.Review.Explanation, StringComparison.OrdinalIgnoreCase);
        var cells = iso.Db.GetPanelCells(outcome.PanelId);
        var cell1 = cells.Single(c => c.CellNumber == "1");
        Assert.DoesNotContain(cell1.Antigens.Keys, k => k.Equals("Fy3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("++", cell1.GetTypedValue("E"));
        Assert.Equal("+", cell1.GetTypedValue("e"));
    }

    [Fact]
    public void Inspector_FlagsRareNullPhenotypes_NotLewisOrUntypedPartner()
    {
        var k0 = new PanelCell { CellNumber = "1" };
        k0.SetAntigen("K", "-");
        k0.SetAntigen("k", "-");
        var lewis = new PanelCell { CellNumber = "2" };
        lewis.SetAntigen("Lea", "-");
        lewis.SetAntigen("Leb", "-");
        var untypedK = new PanelCell { CellNumber = "3" };
        untypedK.SetAntigen("K", "-");
        var jk = new PanelCell { CellNumber = "4" };
        jk.SetAntigen("Jka", "-");
        jk.SetAntigen("Jkb", "-");
        var rhnull = new PanelCell { CellNumber = "5" };
        rhnull.SetAntigen("C", "-");
        rhnull.SetAntigen("c", "-");
        rhnull.SetAntigen("E", "-");
        rhnull.SetAntigen("e", "-");

        var issues = PanelTypingInspector.Inspect(new[] { k0, lewis, untypedK, jk, rhnull });
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.NullPhenotype
            && i.CellNumber == "1" && i.Explanation.Contains("K0"));
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.NullPhenotype
            && i.CellNumber == "4" && i.Explanation.Contains("Jk(a−b−)"));
        Assert.Contains(issues, i => i.Kind == PanelTypingInspector.NullPhenotype
            && i.CellNumber == "5" && i.Explanation.Contains("Rhnull"));
        Assert.DoesNotContain(issues, i => i.CellNumber == "5" && i.Explanation.Contains("types C−c−.")
            && !i.Explanation.Contains("Rhnull"));
        Assert.DoesNotContain(issues, i => i.CellNumber is "2" or "3");
        Assert.DoesNotContain(issues, i => i.Kind == PanelTypingInspector.ImpossibleValue);
    }

    [Fact]
    public void Import_K0LikeCell_StaysInactiveWithNullReview()
    {
        using var iso = new IsolatedDatabase();
        var parsed = ParseCsv("""
            Cell,D,C,c,E,e,K,k
            1,+,-,-,-,+,+,-
            2,-,+,+,+,-,-,-
            """, "LOT-K0", "10-cell");
        var outcome = new VendorPanelImportService(iso.Db).Import(parsed);
        Assert.False(iso.Db.GetPanel(outcome.PanelId)!.IsActive);
        Assert.Contains(outcome.Review.TypingIssues, i => i.Kind == PanelTypingInspector.NullPhenotype
            && i.CellNumber == "2");
        Assert.Contains("K0-like", outcome.Review.Explanation);
        Assert.Contains("Not a diagnosis", outcome.Review.Explanation);
        Assert.DoesNotContain(outcome.Review.TypingIssues, i => i.Kind == PanelTypingInspector.ImpossibleValue);
        var cell2 = iso.Db.GetPanelCells(outcome.PanelId).Single(c => c.CellNumber == "2");
        Assert.Equal("-", cell2.GetTypedValue("K"));
        Assert.Equal("-", cell2.GetTypedValue("k"));
        Assert.False(cell2.IsHomozygousFor("K"));
    }

    [Fact]
    public void Activate_MakesPanelAvailableForInventory()
    {
        using var iso = new IsolatedDatabase();
        var id = new VendorPanelImportService(iso.Db).Persist(
            ParseCsv("Cell,D\n1,+\n", "LOT-ACT", null));
        Assert.Empty(iso.Db.GetActivePanels());

        iso.Db.SetPanelActive(id, true);
        Assert.Contains(iso.Db.GetActivePanels(), p => p.PanelId == id);
        Assert.True(iso.Db.GetPanel(id)!.IsActive);
    }

    private static VendorParseResult ParseCsv(string csv, string lot, string? productLine)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_review_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv);
        try
        {
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = lot;
            parsed.ProductLine = productLine;
            parsed.SourceFormat = "csv";
            return parsed;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
