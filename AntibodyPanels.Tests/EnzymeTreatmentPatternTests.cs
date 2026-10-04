using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class EnzymeTreatmentPatternTests
{
    [Fact]
    public void FicinLossOnFyaCells_IsDestroyedPattern_NotADiagnosis()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "0" },
            ficinAhg: new[] { "0", "0", "0", "0" },
            fya: new[] { "+", "+", "+", "-" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.Equal(3, note.MatchingCells);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("Fya", note.Explanation);
        Assert.Contains("lost reactivity", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.Contains("evidence score", note.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.TreatmentInferences, i =>
            i.Antibody == "anti-Fya" && i.InferenceType == TreatmentInferenceType.ReactivityLostOnEnzyme);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.EnhancedKind);
    }

    [Fact]
    public void FicinGainOnECells_IsEnhancedPattern()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "0", "0", "0", "0" },
            ficinAhg: new[] { "2+", "2+", "2+", "0" },
            fya: new[] { "-", "-", "-", "-" },
            e: new[] { "+", "+", "+", "-" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.EnhancedKind);
        Assert.Contains("became reactive", note.Explanation);
        Assert.Contains("E", note.Explanation);
        Assert.Contains(result.TreatmentInferences, i =>
            i.Antibody == "anti-E" && i.InferenceType == TreatmentInferenceType.ReactivityGainedOnEnzyme);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void FicinPersistenceOnFyaCells_ArguesAgainstDestroyedOnly()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            ficinAhg: new[] { "2+", "2+", "2+", "2+" },
            fya: new[] { "+", "+", "+", "+" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.ResistantKind);
        Assert.Contains("remained reactive", note.Explanation);
        Assert.Contains("argues against", note.Explanation);
        Assert.Contains("Fya", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void MixedLostAndPersisted_IsConflictingDestroyedFit()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+", "2+", "2+" },
            ficinAhg: new[] { "0", "2+", "2+", "2+" },
            fya: new[] { "+", "+", "+", "+" });

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains("1", note.SupportingCells);
        Assert.Contains("2", note.ConflictingCells);
        Assert.Contains("partial", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
    }

    [Fact]
    public void DifferentPanelIds_AreNotPaired()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var ficin = Ctx(2, 99, CellTreatment.Ficin);
        var result = new AnalysisResult();
        EnzymeTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = ficin },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = FyaCells(new[] { "+", "+", "+", "-" }),
                [99] = FyaCells(new[] { "+", "+", "+", "-" })
            });
        Assert.DoesNotContain(result.ReactionPatterns,
            n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void Analyzer_FicinVsUntreated_SurfacesPatternWithoutIdentifying()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ENZ-FYA", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ENZ-FYA", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "Fya", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "Fya", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "Fya", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "Fya", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ENZ-FYA", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var ficin = iso.Db.AddPanelRun("ENZ-FYA", panelId, CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        iso.Db.SaveReaction(ficin, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(ficin, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(ficin, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(ficin, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ENZ-FYA", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains(result.Suggestions, s => s.Contains("enzyme-destroyed") || s.Contains("lost reactivity"));
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("Enzyme/treatment comparison", doc);
        Assert.Contains("Not a diagnosis", doc);
    }

    private static AnalysisResult AnalyzePairs(
        string[] untreatedAhg, string[] ficinAhg, string[] fya, string[]? e = null)
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var ficin = Ctx(2, 1, CellTreatment.Ficin);
        var result = new AnalysisResult();
        EnzymeTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns(untreatedAhg),
                [2] = Rxns(ficinAhg)
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = ficin },
            new Dictionary<int, List<PanelCell>> { [1] = FyaCells(fya, e) });
        return result;
    }

    private static RunContext Ctx(int runId, int panelId, CellTreatment treatment) =>
        new(new PanelRun
        {
            RunId = runId,
            PanelId = panelId,
            CellTreatment = treatment,
            SerumTreatment = SerumTreatment.None,
            Label = treatment == CellTreatment.Ficin ? "Ficin" : "Untreated"
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

    private static List<PanelCell> FyaCells(string[] fya, string[]? e = null) =>
        fya.Select((val, i) =>
        {
            var cell = new PanelCell { CellNumber = (i + 1).ToString() };
            cell.SetAntigen("Fya", val);
            if (e != null && i < e.Length)
                cell.SetAntigen("E", e[i]);
            return cell;
        }).ToList();
}
