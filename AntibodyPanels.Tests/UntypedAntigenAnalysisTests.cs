using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class UntypedAntigenAnalysisTests
{
    [Fact]
    public void SubsetImport_DoesNotTreatMissingColumnsAsNegativeTyping()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e,M,N,S,s\n" +
            "1,+,-,-,+,-,+,+,-,+\n",
            "BIO-UNTYPED-1");
        iso.Db.AddSpecimen("UT-SUBSET", "serum", null);
        iso.Db.LinkSpecimenPanel("UT-SUBSET", panelId);
        iso.Db.SaveReaction("UT-SUBSET", panelId, "1", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UT-SUBSET", updateDb: false);
        var ev = result.RuleoutEvaluations.Single(e => e.Antibody == "anti-E");
        Assert.True(ev.MeetsCriteria);
        Assert.True(result.RuledOut.ContainsKey("anti-E"));
        Assert.DoesNotContain(result.RuleoutEvaluations, e => e.Antibody is "anti-Fya" or "anti-K" or "anti-Jka");
        Assert.DoesNotContain("anti-Fya", result.RuledOut.Keys);
        Assert.DoesNotContain("anti-K", result.RuledOut.Keys);
        Assert.Contains("Fya", result.UntypedClinicallySignificant);
        Assert.Contains("K", result.UntypedClinicallySignificant);
        Assert.DoesNotContain("E", result.UntypedClinicallySignificant);
        Assert.Contains(result.Suggestions, s => s.Contains("does not type") && s.Contains("Fya"));
    }

    [Fact]
    public void EPositiveWithoutEColumn_IsNotHomozygousRuleout()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,K\n" +
            "1,+,-,-,+,-\n",
            "BIO-NO-LITTLE-E");
        iso.Db.AddSpecimen("UT-NO-E", "serum", null);
        iso.Db.LinkSpecimenPanel("UT-NO-E", panelId);
        var cell = iso.Db.GetPanelCells(panelId).Single(c => c.CellNumber == "1");
        Assert.True(cell.HasTypedAntigen("E"));
        Assert.False(cell.HasTypedAntigen("e"));
        iso.Db.SaveReaction("UT-NO-E", panelId, "1", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UT-NO-E", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.DoesNotContain(result.RuleoutEvaluations, e => e.Antibody == "anti-E" && e.MeetsCriteria);
    }

    [Fact]
    public void NtOnE_DoesNotCountAsAntigenNegativeOrRuleout()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,NT,-\n" +
            "2,-,+,+,-,+\n",
            "BIO-E-NT");
        iso.Db.AddSpecimen("UT-E-NT", "serum", null);
        iso.Db.LinkSpecimenPanel("UT-E-NT", panelId);
        var cell1 = iso.Db.GetPanelCells(panelId).Single(c => c.CellNumber == "1");
        Assert.False(cell1.HasTypedAntigen("E"));
        iso.Db.SaveReaction("UT-E-NT", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("UT-E-NT", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UT-E-NT", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        if (result.Suspected.TryGetValue("anti-E", out _))
        {
            var ev = result.SuspectedEvidence["anti-E"];
            Assert.DoesNotContain(ev.ConflictingCells, c => c.CellNumber == "1");
            Assert.DoesNotContain(ev.SupportingCells, c => c.CellNumber == "1");
        }
        Assert.DoesNotContain("E", result.UntypedClinicallySignificant);
    }

    [Fact]
    public void ReactiveEPositive_IsConflict_NotRuleoutCell()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,+,-\n" +
            "2,-,+,+,-,+\n",
            "BIO-E-CONF");
        iso.Db.AddSpecimen("UT-E-CONF", "serum", null);
        iso.Db.LinkSpecimenPanel("UT-E-CONF", panelId);
        iso.Db.SaveReaction("UT-E-CONF", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("UT-E-CONF", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UT-E-CONF", updateDb: false);
        var ev = result.RuleoutEvaluations.SingleOrDefault(e => e.Antibody == "anti-E");
        Assert.True(ev == null || ev.ObservedCount == 0);
        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        if (ev != null)
            Assert.Contains("1", ev.ConflictingReactiveCells);
    }

    private static int PersistSubset(IsolatedDatabase iso, string csv, string lot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_untyped_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, csv);
            using var catalog = new VendorCatalogService();
            var parsed = catalog.ImportFile(VendorIds.BioRad, path);
            Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
            parsed.Vendor = VendorIds.BioRad;
            parsed.LotNumber = lot;
            parsed.SourceFormat = "csv";
            return new VendorPanelImportService(iso.Db).Persist(parsed);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
