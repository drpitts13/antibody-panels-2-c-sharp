using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Services.Vendors;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class UntypeableSpecificityTests
{
    [Fact]
    public void Classifier_EmptyTypedSet_DoesNotInventGaps()
    {
        var list = UntypeableSpecificityClassifier.Classify(Array.Empty<string>());
        Assert.Empty(list);
    }

    [Fact]
    public void Classifier_TypedEOnly_FlagsFyaAndKpa_NotE()
    {
        var list = UntypeableSpecificityClassifier.Classify(new[] { "E", "e" });
        Assert.Contains(list, u => u.Antibody == "anti-Fya" && u.IsClinicallySignificant);
        Assert.Contains(list, u => u.Antibody == "anti-Kpa" && !u.IsClinicallySignificant);
        Assert.DoesNotContain(list, u => u.Antibody == "anti-E");
        Assert.DoesNotContain(list, u => u.Antigen == "Vel");
        Assert.Contains("never imported", list.Single(u => u.Antibody == "anti-Fya").Explanation);
        Assert.Contains("not a rule-out", list.Single(u => u.Antibody == "anti-Fya").Explanation);
    }

    [Fact]
    public void SubsetPanel_CannotTypeFyaOrKpa_WhileEIsEvaluated()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,+,- \n" +
            "2,-,+,+,-,+\n",
            "BIO-UNTYPE-1");
        iso.Db.AddSpecimen("UTYPE-E", "serum", null);
        iso.Db.LinkSpecimenPanel("UTYPE-E", panelId);
        iso.Db.SaveReaction("UTYPE-E", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("UTYPE-E", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UTYPE-E", updateDb: false);
        Assert.Contains(result.UntypeableSpecificities, u => u.Antibody == "anti-Fya");
        Assert.Contains(result.UntypeableSpecificities, u => u.Antibody == "anti-Kpa");
        Assert.DoesNotContain(result.UntypeableSpecificities, u => u.Antibody == "anti-E");
        Assert.DoesNotContain("anti-Fya", result.Suspected.Keys);
        Assert.DoesNotContain("anti-Fya", result.RuledOut.Keys);

        var fya = RequireExplanation(result, "anti-Fya");
        Assert.Equal("Cannot type from current panels", fya.Status);
        Assert.Contains("never imported", fya.Narrative);
        Assert.Contains("not an identification", fya.Narrative);
        Assert.Contains("Fya", fya.AdditionalTesting);
        Assert.Empty(fya.SupportingCells);
        Assert.Empty(fya.RuleoutCells);

        var kpa = RequireExplanation(result, "anti-Kpa");
        Assert.Equal("Cannot type from current panels", kpa.Status);

        var e = RequireExplanation(result, "anti-E");
        Assert.NotEqual("Cannot type from current panels", e.Status);
        Assert.Contains(result.Suggestions, s => s.Contains("does not type") && s.Contains("Fya"));
        Assert.Contains(result.Suggestions, s => s.Contains("anti-Kpa") && s.Contains("never imported"));

        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("cannot type", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anti-Fya", doc);
        Assert.Contains("anti-Kpa", doc);
        Assert.Contains("Decision support", doc);
    }

    [Fact]
    public void ConflictingEPattern_DoesNotInventFyaSupport()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,+,- \n" +
            "2,+,-,-,+,- \n" +
            "3,-,+,+,-,+\n",
            "BIO-UNTYPE-CONF");
        iso.Db.AddSpecimen("UTYPE-CONF", "serum", null);
        iso.Db.LinkSpecimenPanel("UTYPE-CONF", panelId);
        iso.Db.SaveReaction("UTYPE-CONF", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("UTYPE-CONF", panelId, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction("UTYPE-CONF", panelId, "3", "0", "0", "2+", "NT");

        var result = iso.Analyzer.AnalyzeSpecimen("UTYPE-CONF", updateDb: false);
        var e = RequireExplanation(result, "anti-E");
        Assert.NotEqual("Cannot type from current panels", e.Status);
        Assert.True(e.ConflictingCells.Count + e.SupportingCells.Count + e.RuleoutCells.Count > 0);
        Assert.False(result.SuspectedEvidence.ContainsKey("anti-Fya"));
        var fya = RequireExplanation(result, "anti-Fya");
        Assert.Equal("Cannot type from current panels", fya.Status);
        Assert.Empty(fya.SupportingCells);
        Assert.Empty(fya.ConflictingCells);
        Assert.False(result.Suspected.ContainsKey("anti-Fya"));
    }

    [Fact]
    public void HistoricalKpa_WithoutColumn_IsHistoricalAndUntypeable()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e\n" +
            "1,+,-,-,-,+\n",
            "BIO-UNTYPE-HIST");
        iso.Db.AddSpecimen("UTYPE-HIST", "serum", null, notes: null, phenotype: null,
            previousAntibodies: "anti-Kpa");
        iso.Db.LinkSpecimenPanel("UTYPE-HIST", panelId);
        iso.Db.SaveReaction("UTYPE-HIST", panelId, "1", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UTYPE-HIST", updateDb: false);
        var exp = RequireExplanation(result, "anti-Kpa");
        Assert.Equal("Historical — cannot type from current panels", exp.Status);
        Assert.Contains("previously identified", exp.Narrative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never imported", exp.Narrative);
        Assert.False(result.Suspected.ContainsKey("anti-Kpa"));
        Assert.DoesNotContain("anti-Kpa", result.RuledOut.Keys);
    }

    [Fact]
    public void TypedKpaColumn_IsNotUntypeable()
    {
        using var iso = new IsolatedDatabase();
        var panelId = PersistSubset(iso,
            "Cell,D,C,c,E,e,Kpa\n" +
            "1,+,-,-,-,+,-\n" +
            "2,-,+,+,-,+,+\n",
            "BIO-UNTYPE-KPA");
        iso.Db.AddSpecimen("UTYPE-KPA", "serum", null);
        iso.Db.LinkSpecimenPanel("UTYPE-KPA", panelId);
        iso.Db.SaveReaction("UTYPE-KPA", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("UTYPE-KPA", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("UTYPE-KPA", updateDb: false);
        Assert.DoesNotContain(result.UntypeableSpecificities, u => u.Antibody == "anti-Kpa");
        Assert.DoesNotContain(result.CandidateExplanations,
            e => e.Antibody == "anti-Kpa" && e.Status.Contains("Cannot type"));
    }

    private static CandidateExplanation RequireExplanation(AnalysisResult result, string antibody)
    {
        var exp = result.CandidateExplanations
            .SingleOrDefault(e => string.Equals(e.Antibody, antibody, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(exp);
        return exp!;
    }

    private static int PersistSubset(IsolatedDatabase iso, string csv, string lot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vendor_untype_{Guid.NewGuid():N}.csv");
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
