using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class DttTreatmentPatternTests
{
    [Fact]
    public void DttLossOnKCells_IsDestroyedPattern_NotADiagnosis()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "0" },
            dttAhg: new[] { "0", "0", "0", "0" },
            k: new[] { "+", "+", "+", "-" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
        Assert.Equal(3, note.MatchingCells);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("K", note.Explanation);
        Assert.Contains("lost reactivity", note.Explanation);
        Assert.Contains("DTT-destroyed", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.Contains("evidence score", note.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.TreatmentInferences, i =>
            i.Antibody == "anti-K" && i.InferenceType == TreatmentInferenceType.ReactivityLostOnDTT);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.ResistantKind);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void DttPersistenceOnKCells_ArguesAgainstKellOnly()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            dttAhg: new[] { "2+", "2+", "2+", "2+" },
            k: new[] { "+", "+", "+", "+" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.ResistantKind);
        Assert.Contains("remained reactive", note.Explanation);
        Assert.Contains("argues against", note.Explanation);
        Assert.Contains("K", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void MixedLostAndPersisted_IsConflictingDestroyedFit()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            dttAhg: new[] { "0", "2+", "2+", "2+" },
            k: new[] { "+", "+", "+", "+" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("2", note.ConflictingCells);
        Assert.Contains("partial", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
    }

    [Fact]
    public void DifferentPanelIds_AreNotPaired()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var dtt = Ctx(2, 99, CellTreatment.DTT);
        var result = new AnalysisResult();
        DttTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = dtt },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = KCells(new[] { "+", "+", "+", "-" }),
                [99] = KCells(new[] { "+", "+", "+", "-" })
            });
        Assert.DoesNotContain(result.ReactionPatterns,
            n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void FicinOnlyRuns_DoNotProduceDttKinds()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var ficin = Ctx(2, 1, CellTreatment.Ficin);
        var result = new AnalysisResult();
        DttTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = ficin },
            new Dictionary<int, List<PanelCell>> { [1] = KCells(new[] { "+", "+", "+", "-" }) });
        Assert.Empty(result.ReactionPatterns);
    }

    [Fact]
    public void Analyzer_DttVsUntreated_SurfacesPatternWithoutIdentifying()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DTT-K", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("DTT-K", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "K", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "K", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "K", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "K", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("DTT-K", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var dtt = iso.Db.AddPanelRun("DTT-K", panelId, CellTreatment.DTT, SerumTreatment.None, "DTT");
        iso.Db.SaveReaction(dtt, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(dtt, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(dtt, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(dtt, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("DTT-K", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains(result.Suggestions, s => s.Contains("DTT-destroyed") || s.Contains("lost reactivity"));
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("Enzyme/treatment comparison", doc);
        Assert.Contains("DTT-destroyed", doc);
        Assert.Contains("Not a diagnosis", doc);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void LutheranLossOnDtt_NamesLub()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var dtt = Ctx(2, 1, CellTreatment.DTT);
        var result = new AnalysisResult();
        var cells = Enumerable.Range(0, 4).Select(i =>
        {
            var cell = new PanelCell { CellNumber = (i + 1).ToString() };
            cell.SetAntigen("Lub", i < 3 ? "+" : "-");
            return cell;
        }).ToList();
        DttTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = dtt },
            new Dictionary<int, List<PanelCell>> { [1] = cells });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == DttTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains("Lub", note.Explanation);
        Assert.Contains(result.TreatmentInferences, i =>
            i.Antibody == "anti-Lub" && i.InferenceType == TreatmentInferenceType.ReactivityLostOnDTT);
    }

    private static AnalysisResult AnalyzePairs(string[] untreatedAhg, string[] dttAhg, string[] k)
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var dtt = Ctx(2, 1, CellTreatment.DTT);
        var result = new AnalysisResult();
        DttTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns(untreatedAhg),
                [2] = Rxns(dttAhg)
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = dtt },
            new Dictionary<int, List<PanelCell>> { [1] = KCells(k) });
        return result;
    }

    private static RunContext Ctx(int runId, int panelId, CellTreatment treatment) =>
        new(new PanelRun
        {
            RunId = runId,
            PanelId = panelId,
            CellTreatment = treatment,
            SerumTreatment = SerumTreatment.None,
            Label = treatment == CellTreatment.DTT ? "DTT" : "Untreated"
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

    private static List<PanelCell> KCells(string[] k) =>
        k.Select((val, i) =>
        {
            var cell = new PanelCell { CellNumber = (i + 1).ToString() };
            cell.SetAntigen("K", val);
            return cell;
        }).ToList();
}
