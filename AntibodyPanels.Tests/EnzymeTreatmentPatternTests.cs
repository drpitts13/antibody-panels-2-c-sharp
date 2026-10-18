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
        var unpaired = Assert.Single(result.ReactionPatterns, n =>
            n.Kind == EnzymeTreatmentPatternClassifier.UnpairedKind);
        Assert.Contains("Ficin on panel 99 was not compared", unpaired.Explanation);
        Assert.Contains("panel 1 were not used", unpaired.Explanation);
        Assert.Contains("A different panel was not paired", unpaired.Explanation);
        Assert.Contains("Not a diagnosis", unpaired.Explanation);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("was not compared", doc);
        Assert.Contains("Not a diagnosis", doc);
    }

    [Fact]
    public void FicinWithoutAnyUntreatedRun_SaysItWasNotCompared()
    {
        var ficin = Ctx(2, 5, CellTreatment.Ficin);
        var result = new AnalysisResult();
        EnzymeTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>> { [2] = Rxns("0", "0", "0", "0") },
            new Dictionary<int, RunContext> { [2] = ficin },
            new Dictionary<int, List<PanelCell>> { [5] = FyaCells(new[] { "+", "+", "+", "-" }) });

        var unpaired = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.UnpairedKind, unpaired.Kind);
        Assert.Contains("No untreated run was recorded", unpaired.Explanation);
        Assert.Contains("Not a diagnosis", unpaired.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
    }

    [Fact]
    public void PapainOnAnotherPanel_IsNotPaired_AndSamePanelFicinStillCompares()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var ficin = Ctx(2, 1, CellTreatment.Ficin);
        var papain = new RunContext(new PanelRun
        {
            RunId = 3,
            PanelId = 7,
            CellTreatment = CellTreatment.Papain,
            Label = "Papain"
        });
        var result = new AnalysisResult();
        EnzymeTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+", "2+", "0"),
                [2] = Rxns("0", "0", "0", "0"),
                [3] = Rxns("0", "0", "0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = ficin, [3] = papain },
            new Dictionary<int, List<PanelCell>>
            {
                [1] = FyaCells(new[] { "+", "+", "+", "-" }),
                [7] = FyaCells(new[] { "+", "+", "+", "-" })
            });

        Assert.Contains(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        var unpaired = Assert.Single(result.ReactionPatterns, n =>
            n.Kind == EnzymeTreatmentPatternClassifier.UnpairedKind);
        Assert.Contains("Papain on panel 7 was not compared", unpaired.Explanation);
        Assert.Contains("panel 1 were not used", unpaired.Explanation);
        Assert.DoesNotContain(unpaired.Explanation, "Fya", StringComparison.Ordinal);
    }

    [Fact]
    public void SamePanelWithTooFewPairedCells_IsIncomplete_NotUnpairedOrDestroyed()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "0" },
            ficinAhg: new[] { "0", "0" },
            fya: new[] { "+", "-" });

        var note = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.IncompleteKind, note.Kind);
        Assert.Contains("1 cell(s) could be paired (1)", note.Explanation);
        Assert.Contains("1 lost reactivity", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.Contains("No enzyme-destroyed, enzyme-enhanced, or enzyme-resistant pattern was assigned", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.UnpairedKind);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("comparison is incomplete", doc);
        Assert.Contains("Not a diagnosis", doc);
    }

    [Fact]
    public void TwoCellsThatLookDestroyed_StayIncomplete()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "2+" },
            ficinAhg: new[] { "0", "0" },
            fya: new[] { "+", "+" });

        var note = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.IncompleteKind, note.Kind);
        Assert.Contains("2 cell(s) could be paired (1, 2)", note.Explanation);
        Assert.Contains("2 lost reactivity", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.DoesNotContain(note.Explanation, "consistent with antibodies", StringComparison.Ordinal);
    }

    [Fact]
    public void MixedLossAndGainOnTwoCells_IsIncomplete_NotAPattern()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "2+", "0" },
            ficinAhg: new[] { "0", "2+" },
            fya: new[] { "+", "-" });

        var note = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.IncompleteKind, note.Kind);
        Assert.Contains("1 lost reactivity", note.Explanation);
        Assert.Contains("1 gained reactivity", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.EnhancedKind);
    }

    [Fact]
    public void AllNonreactivePairs_AreIncompleteWithNoCells()
    {
        var result = AnalyzePairs(
            untreatedAhg: new[] { "0", "0", "0" },
            ficinAhg: new[] { "0", "0", "0" },
            fya: new[] { "+", "+", "-" });

        var note = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.IncompleteKind, note.Kind);
        Assert.Contains("0 cell(s) could be paired (none)", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.ResistantKind);
    }

    [Fact]
    public void PapainWithTwoPairedCells_IsIncomplete()
    {
        var untreated = Ctx(1, 1, CellTreatment.None);
        var papain = new RunContext(new PanelRun
        {
            RunId = 2,
            PanelId = 1,
            CellTreatment = CellTreatment.Papain,
            Label = "Papain"
        });
        var result = new AnalysisResult();
        EnzymeTreatmentPatternClassifier.Apply(
            result,
            new Dictionary<int, List<Reaction>>
            {
                [1] = Rxns("2+", "2+"),
                [2] = Rxns("0", "0")
            },
            new Dictionary<int, RunContext> { [1] = untreated, [2] = papain },
            new Dictionary<int, List<PanelCell>> { [1] = FyaCells(new[] { "+", "+" }) });

        var note = Assert.Single(result.ReactionPatterns);
        Assert.Equal(EnzymeTreatmentPatternClassifier.IncompleteKind, note.Kind);
        Assert.Contains("Papain on panel 1", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
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

    [Fact]
    public void Analyzer_FicinOnAnotherPanel_ExplainsThatItWasNotCompared()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ENZ-UNP", "serum", null);
        var panelA = iso.Db.AddPanel("A", "L1", "V", 4, null, false);
        var panelB = iso.Db.AddPanel("B", "L2", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ENZ-UNP", panelA);
        iso.Db.LinkSpecimenPanel("ENZ-UNP", panelB);
        var untreated = iso.Db.GetOrCreateDefaultRun("ENZ-UNP", panelA);
        foreach (var cell in new[] { "1", "2", "3", "4" })
            iso.Db.SaveReaction(untreated, cell, "0", "0", cell == "4" ? "0" : "2+", "NT");
        var ficin = iso.Db.AddPanelRun("ENZ-UNP", panelB, CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        foreach (var cell in new[] { "1", "2", "3", "4" })
            iso.Db.SaveReaction(ficin, cell, "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ENZ-UNP", updateDb: false);
        var unpaired = Assert.Single(result.ReactionPatterns, n =>
            n.Kind == EnzymeTreatmentPatternClassifier.UnpairedKind);
        Assert.Contains("was not compared", unpaired.Explanation);
        Assert.Contains("A different panel was not paired", unpaired.Explanation);
        Assert.Contains("Not a diagnosis", unpaired.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains(result.Suggestions, s => s.Contains("was not compared") && s.Contains("Not a diagnosis"));
        var report = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ENZ-UNP");
        Assert.Contains("was not compared", report);
        Assert.Contains("Not a diagnosis", report);
    }

    [Fact]
    public void Analyzer_TwoPairedFicinCells_ExplainsIncompleteComparison()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ENZ-SHORT", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("ENZ-SHORT", panelId);
        var untreated = iso.Db.GetOrCreateDefaultRun("ENZ-SHORT", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        var ficin = iso.Db.AddPanelRun("ENZ-SHORT", panelId, CellTreatment.Ficin, SerumTreatment.None, "Ficin");
        iso.Db.SaveReaction(ficin, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(ficin, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ENZ-SHORT", updateDb: false);
        var note = Assert.Single(result.ReactionPatterns, n =>
            n.Kind == EnzymeTreatmentPatternClassifier.IncompleteKind);
        Assert.Contains("2 cell(s) could be paired", note.Explanation);
        Assert.Contains("2 lost reactivity", note.Explanation);
        Assert.Contains("The comparison is incomplete", note.Explanation);
        Assert.Contains("Not a diagnosis", note.Explanation);
        Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == EnzymeTreatmentPatternClassifier.DestroyedKind);
        Assert.Contains(result.Suggestions, s => s.Contains("comparison is incomplete") && s.Contains("Not a diagnosis"));
        var report = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ENZ-SHORT");
        Assert.Contains("comparison is incomplete", report);
        Assert.Contains("Not a diagnosis", report);
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
