using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class AbsorptionPatternTests
{
    [Fact]
    public void RrAdsorption_LossOnCPositiveCells_SupportsRemoval_NotADiagnosis()
    {
        var result = AnalyzeAllo(
            SerumTreatment.AlloAdsorptionRr,
            "Absorbed: rr",
            untreatedAhg: new[] { "2+", "2+", "2+", "0" },
            adsorbedAhg: new[] { "0", "0", "0", "0" },
            c: new[] { "++", "+", "+", "-" },
            e: new[] { "-", "+", "+", "-" });

        var note = Assert.Single(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.RemovedKind);
        Assert.Contains("1 (homozygous)", note.SupportingCells);
        Assert.Contains("2", note.SupportingCells);
        Assert.Empty(note.ConflictingCells);
        Assert.Contains("lost on", note.Explanation);
        Assert.Contains("anti-c", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.Contains("evidence score", note.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, note.EvidenceScore);
        Assert.DoesNotContain(result.AbsorptionConclusions, n => n.Kind == AbsorptionPatternClassifier.SurvivedKind && n.Antibody == "anti-c");

        var explained = AnalysisExplainer.Build(result);
        var antiC = Assert.Single(explained, e => e.Antibody == "anti-c");
        Assert.Contains("1 (homozygous)", antiC.Narrative);
        Assert.Contains("Absorption evidence", antiC.Status);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("Absorption comparison", doc);
        Assert.Contains("anti-c", doc);
        Assert.DoesNotContain("identified", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RrAdsorption_CPositiveCellsRemainReactive_ArguesAgainstRemoval()
    {
        var result = AnalyzeAllo(
            SerumTreatment.AlloAdsorptionRr,
            "Absorbed: rr",
            untreatedAhg: new[] { "2+", "2+", "2+", "0" },
            adsorbedAhg: new[] { "2+", "1+", "2+", "0" },
            c: new[] { "+", "+", "+", "-" },
            e: new[] { "-", "-", "-", "+" });

        var note = Assert.Single(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.SurvivedKind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("remained reactive", note.Explanation);
        Assert.Contains("argues against", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.DoesNotContain(result.AbsorptionConclusions, n => n.Kind == AbsorptionPatternClassifier.RemovedKind);
    }

    [Fact]
    public void MixedLossAndPersistence_IsConflictingAbsorptionEvidence()
    {
        var result = AnalyzeAllo(
            SerumTreatment.AlloAdsorptionRr,
            "Absorbed: rr",
            untreatedAhg: new[] { "2+", "2+", "2+", "0" },
            adsorbedAhg: new[] { "0", "2+", "2+", "0" },
            c: new[] { "+", "+", "+", "-" },
            e: new[] { "-", "-", "-", "-" });

        var note = Assert.Single(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.MixedKind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("2", note.ConflictingCells);
        Assert.Contains("3", note.ConflictingCells);
        Assert.Contains("Conflicting absorption evidence", note.Explanation);
        Assert.Contains("do not treat anti-c as removed", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
    }

    [Fact]
    public void ResidualCellLackingRemovedAntigens_IsNotExplainedByThoseSpecificities()
    {
        var result = AnalyzeAllo(
            SerumTreatment.AlloAdsorptionRr,
            "Absorbed: rr",
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            adsorbedAhg: new[] { "0", "0", "0", "2+" },
            c: new[] { "+", "+", "+", "-" },
            e: new[] { "+", "+", "+", "-" });

        Assert.Contains(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.RemovedKind);
        var residual = Assert.Single(result.AbsorptionConclusions, n =>
            n.Kind == AbsorptionPatternClassifier.ResidualKind);
        Assert.Contains("4", residual.SupportingCells);
        Assert.Contains("not explained", residual.Explanation);
        Assert.Contains("Not a diagnosis", residual.Explanation);
    }

    [Fact]
    public void SingleAntigenPositiveCell_DoesNotStateRemoval()
    {
        var result = AnalyzeAllo(
            SerumTreatment.AlloAdsorptionRr,
            "Absorbed: rr",
            untreatedAhg: new[] { "2+", "0", "0", "0" },
            adsorbedAhg: new[] { "0", "0", "0", "0" },
            c: new[] { "+", "-", "-", "-" },
            e: new[] { "-", "-", "-", "-" });

        Assert.DoesNotContain(result.AbsorptionConclusions, n => n.Antibody == "anti-c");
    }

    [Fact]
    public void DifferentPanelIds_AreNotPaired()
    {
        var untreated = Ctx(1, 1, SerumTreatment.None, "Untreated");
        var adsorbed = Ctx(2, 99, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        var result = new AnalysisResult();
        AbsorptionPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = adsorbed },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = RhCells(new[] { "+", "+", "+", "-" }, new[] { "-", "-", "-", "-" }),
                [99] = RhCells(new[] { "+", "+", "+", "-" }, new[] { "-", "-", "-", "-" })
            });
        Assert.Empty(result.AbsorptionConclusions);
    }

    [Fact]
    public void FicinWithoutAdsorption_DoesNotProduceAbsorptionKinds()
    {
        var untreated = Ctx(1, 1, SerumTreatment.None, "Untreated");
        var ficin = new RunContext(new PanelRun
        {
            RunId = 2,
            PanelId = 1,
            CellTreatment = CellTreatment.Ficin,
            SerumTreatment = SerumTreatment.None,
            Label = "Ficin"
        });
        var result = new AnalysisResult();
        AbsorptionPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = ficin },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = RhCells(new[] { "+", "+", "+", "-" }, new[] { "-", "-", "-", "-" })
            });
        Assert.Empty(result.AbsorptionConclusions);
    }

    [Fact]
    public void Autoadsorption_LossOnPairedCells_IsPanreactiveRemovalEvidence()
    {
        var result = AnalyzeAuto(
            untreatedAhg: new[] { "2+", "2+", "2+", "1+" },
            adsorbedAhg: new[] { "0", "0", "0", "0" });

        var note = Assert.Single(result.AbsorptionConclusions);
        Assert.Equal(AbsorptionPatternClassifier.AutoRemovedKind, note.Kind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("panreactive", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.Empty(note.ConflictingCells);
    }

    [Fact]
    public void Autoadsorption_ResidualCell_IsConflictingWithRemoval()
    {
        var result = AnalyzeAuto(
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            adsorbedAhg: new[] { "0", "0", "0", "2+" });

        var note = Assert.Single(result.AbsorptionConclusions);
        Assert.Equal(AbsorptionPatternClassifier.AutoResidualKind, note.Kind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("4", note.ConflictingCells);
        Assert.Contains("Residual reactivity", note.Explanation);
        Assert.Contains("underlying alloantibody", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
    }

    [Fact]
    public void Analyzer_RrVsUntreated_ExplainsRemovalAndDoesNotRuleOutFromTheAliquot()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-C", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-C", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "++");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-C", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun("ABS-C", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-C", updateDb: false);
        var note = Assert.Single(result.AbsorptionConclusions, n => n.Antibody == "anti-c");
        Assert.Equal(AbsorptionPatternClassifier.RemovedKind, note.Kind);
        Assert.Contains("1 (homozygous)", note.Explanation);
        Assert.Contains(result.Suggestions, s => s.Contains("anti-c") && s.Contains("lost on"));
        Assert.Contains(result.GatedRuleouts, g => g.Antibody == "anti-c" && g.Reason.Contains("cannot rule it out"));
        Assert.DoesNotContain(result.RuledOut.Keys, k => k == "anti-c");
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("Absorption comparison", doc);
        Assert.Contains("Not a diagnosis", doc);
        var antiC = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-c");
        Assert.Contains("homozygous", antiC.Narrative);
        var report = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ABS-C");
        Assert.Contains("Absorption comparison", report);
        Assert.Contains("cannot rule it out", report);
    }

    private static AnalysisResult AnalyzeAllo(
        SerumTreatment serum, string label, string[] untreatedAhg, string[] adsorbedAhg, string[] c, string[] e)
    {
        var result = new AnalysisResult();
        AbsorptionPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns(untreatedAhg),
                [2] = Rxns(adsorbedAhg)
            },
            new Dictionary<int, RunContext>
            {
                [1] = Ctx(1, 1, SerumTreatment.None, "Untreated"),
                [2] = Ctx(2, 1, serum, label)
            },
            new Dictionary<int, List<PanelCell>> { [1] = RhCells(c, e) });
        return result;
    }

    private static AnalysisResult AnalyzeAuto(string[] untreatedAhg, string[] adsorbedAhg)
    {
        var result = new AnalysisResult();
        AbsorptionPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns(untreatedAhg),
                [2] = Rxns(adsorbedAhg)
            },
            new Dictionary<int, RunContext>
            {
                [1] = Ctx(1, 1, SerumTreatment.None, "Untreated"),
                [2] = Ctx(2, 1, SerumTreatment.AutoAdsorption, "Autoadsorbed")
            },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = RhCells(new[] { "+", "+", "+", "+" }, new[] { "+", "+", "+", "+" })
            });
        return result;
    }

    private static RunContext Ctx(int runId, int panelId, SerumTreatment serum, string label) =>
        new(new PanelRun
        {
            RunId = runId,
            PanelId = panelId,
            CellTreatment = CellTreatment.None,
            SerumTreatment = serum,
            Label = label
        });

    private static List<Reaction> Rxns(params string[] ahg) =>
        ahg.Select((grade, i) => new Reaction
        {
            CellNumber = (i + 1).ToString(),
            IS = "0",
            C37 = "0",
            AHG = grade,
            CC = grade == "0" ? "2+" : "NT"
        }).ToList();

    private static List<PanelCell> RhCells(string[] c, string[] e) =>
        c.Select((value, i) =>
        {
            var cell = new PanelCell { CellNumber = (i + 1).ToString() };
            cell.SetAntigen("c", value);
            cell.SetAntigen("e", e[i]);
            return cell;
        }).ToList();
}
