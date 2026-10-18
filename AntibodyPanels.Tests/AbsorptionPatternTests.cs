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

    [Fact]
    public void RrAliquotNegatives_DoNotDiluteOrdinaryAntiCProbability()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-SCORE", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-SCORE", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "++");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-SCORE", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-SCORE", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-SCORE");
        var stats = result.SuspectedStatistics["anti-c"];
        Assert.Equal(0.25, stats.FisherPValue, 2);
        Assert.Equal(3, stats.PositiveAgPositiveCount);
        Assert.Equal(1, stats.NegativeAgNegativeCount);
        var evidence = result.SuspectedEvidence["anti-c"];
        Assert.Equal(3, evidence.SupportingCells.Count);
        Assert.All(evidence.SupportingCells, c => Assert.Equal("Untreated", c.RunLabel));
        Assert.Contains(result.AdsorptionScoreExclusions, s =>
            s.Contains("Absorbed: rr is expected to remove anti-c;", StringComparison.Ordinal) &&
            s.Contains("excluded from ordinary probability and pattern scores", StringComparison.Ordinal) &&
            s.Contains("Not a diagnosis", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(3, pattern.Matches);
        Assert.Equal(0, pattern.Mismatches);
        var antiC = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-c");
        Assert.Contains("excluded from ordinary probability and pattern scores", antiC.Narrative);
        Assert.Contains("1 (homozygous)", antiC.Narrative);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("Ordinary scores excluded these reactions", doc);
        Assert.DoesNotContain("identified as", doc, StringComparison.OrdinalIgnoreCase);

        var snap = iso.Db.GetLatestAnalysisSnapshot("ABS-SCORE");
        Assert.False(string.IsNullOrWhiteSpace(snap!.AbsorptionJson));
        var audit = System.Text.Json.JsonSerializer.Deserialize<AdsorptionAudit>(snap.AbsorptionJson!);
        Assert.NotNull(audit);
        Assert.Contains(audit!.Conclusions, c =>
            c.Antibody == "anti-c" && c.Kind == AbsorptionPatternClassifier.RemovedKind);
        Assert.Contains(audit.OrdinaryScoreExclusions, s => s.Contains("remove anti-c;", StringComparison.Ordinal));
        var trace = iso.Reports.GeneratePreviewText(ReportType.AnalysisResults, "ABS-SCORE");
        Assert.Contains("Absorption comparison stored", trace);
        var clinical = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ABS-SCORE");
        Assert.Contains("excluded from ordinary probability and pattern scores", clinical);
        Assert.Contains("Not a diagnosis", clinical);
    }

    [Fact]
    public void MixedAdsorption_SurvivingPositives_AreNotOrdinarySupportingCells()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-MIX", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-MIX", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-MIX", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-MIX", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-MIX", updateDb: false);
        var mixed = Assert.Single(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.MixedKind);
        Assert.Contains(mixed.SupportingCells, s => s.StartsWith("1", StringComparison.Ordinal));
        Assert.Contains(mixed.ConflictingCells, s => s.StartsWith("2", StringComparison.Ordinal));
        Assert.Contains(mixed.ConflictingCells, s => s.StartsWith("3", StringComparison.Ordinal));
        Assert.Contains("Conflicting absorption evidence", mixed.Explanation);
        var evidence = result.SuspectedEvidence["anti-c"];
        Assert.Equal(3, evidence.SupportingCells.Count);
        Assert.All(evidence.SupportingCells, c => Assert.Equal("Untreated", c.RunLabel));
        Assert.DoesNotContain(evidence.SupportingCells, c => c.RunLabel.Contains("Absorbed", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(3, pattern.Matches);
        Assert.Contains(result.AdsorptionScoreExclusions, s => s.Contains("remove anti-c;", StringComparison.Ordinal));
        var antiC = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-c");
        Assert.Contains("do not treat anti-c as removed", antiC.Narrative);
        Assert.Contains("excluded from ordinary probability and pattern scores", antiC.Narrative);
        Assert.Contains("Not a diagnosis", antiC.Narrative);
    }

    [Fact]
    public void RrAliquot_StillScoresAntibodiesItIsNotExpectedToRemove()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-K", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-K", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "++");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "K", "+");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-K", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-K", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-K", updateDb: false);
        Assert.Contains(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.RemovedKind);
        var cPattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(2, cPattern.Matches);
        Assert.Equal(1, cPattern.Mismatches);
        Assert.Contains(result.AdsorptionScoreExclusions, s => s.Contains("remove anti-c;", StringComparison.Ordinal));
        Assert.DoesNotContain(result.AdsorptionScoreExclusions, s => s.Contains("remove anti-K;", StringComparison.Ordinal));
        var kPattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-K");
        Assert.Equal(2, kPattern.Matches);
        Assert.Equal(2, kPattern.Mismatches);
        var kEvidence = result.SuspectedEvidence["anti-K"];
        Assert.Contains(kEvidence.SupportingCells, c => c.RunLabel == "Absorbed: rr" && c.CellNumber == "3");
        Assert.Contains(kEvidence.SupportingCells, c => c.RunLabel == "Untreated" && c.CellNumber == "3");
    }

    [Fact]
    public void Autoadsorption_DoesNotExcludeAlloSpecificitiesFromOrdinaryScores()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-AUTO", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-AUTO", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-AUTO", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "2+", "NT");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-AUTO", panelId, CellTreatment.None, SerumTreatment.AutoAdsorption, "Autoadsorbed");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-AUTO", updateDb: false);
        Assert.Contains(result.AbsorptionConclusions, n => n.Kind == AbsorptionPatternClassifier.AutoResidualKind);
        Assert.Contains(result.AdsorptionScoreExclusions, s =>
            s.Contains("Untreated is panreactive (4 of 4 evaluated cells)", StringComparison.Ordinal) &&
            s.Contains("paired with Autoadsorbed", StringComparison.Ordinal) &&
            s.Contains("excluded from ordinary probability and pattern scores", StringComparison.Ordinal) &&
            s.Contains("Not a diagnosis", StringComparison.Ordinal));
        Assert.DoesNotContain(result.AdsorptionScoreExclusions, s =>
            s.Contains("remove anti-c;", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(2, pattern.Matches);
        Assert.Equal(0, pattern.Mismatches);
        Assert.True(result.Suspected.ContainsKey("anti-c"));
        var evidence = result.SuspectedEvidence["anti-c"];
        Assert.Equal(2, evidence.SupportingCells.Count);
        Assert.All(evidence.SupportingCells, c => Assert.Equal("Autoadsorbed", c.RunLabel));
        var antiC = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-c");
        Assert.Contains("is panreactive", antiC.Narrative);
        Assert.Contains("Not a diagnosis", antiC.Narrative);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("underlying alloantibody", doc);
        Assert.Contains("is panreactive (4 of 4 evaluated cells)", doc);
        Assert.Contains("Not a diagnosis", doc);
    }

    [Fact]
    public void Autoadsorption_ThreeOfFourReactive_StillUsesTheUntreatedRun()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-AUTO-3", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-AUTO-3", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-AUTO-3", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-AUTO-3", panelId, CellTreatment.None, SerumTreatment.AutoAdsorption, "Autoadsorbed");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-AUTO-3", updateDb: false);
        Assert.DoesNotContain(result.AdsorptionScoreExclusions, s => s.Contains("is panreactive", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(4, pattern.Matches);
        Assert.Equal(1, pattern.Mismatches);
        var evidence = result.SuspectedEvidence["anti-c"];
        Assert.Contains(evidence.SupportingCells, c => c.RunLabel == "Untreated");
        Assert.Contains(evidence.SupportingCells, c => c.RunLabel == "Autoadsorbed");
    }

    [Fact]
    public void Autoadsorption_WhenAliquotStaysPanreactive_DoesNotDoubleCountTheUntreatedRun()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-AUTO-BOTH", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-AUTO-BOTH", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-AUTO-BOTH", panelId);
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-AUTO-BOTH", panelId, CellTreatment.None, SerumTreatment.AutoAdsorption, "Autoadsorbed");
        foreach (var runId in new[] { untreated, adsorbed })
        {
            iso.Db.SaveReaction(runId, "1", "0", "0", "2+", "NT");
            iso.Db.SaveReaction(runId, "2", "0", "0", "2+", "NT");
            iso.Db.SaveReaction(runId, "3", "0", "0", "2+", "NT");
            iso.Db.SaveReaction(runId, "4", "0", "0", "2+", "NT");
        }

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-AUTO-BOTH", updateDb: false);
        Assert.Contains(result.AbsorptionConclusions, n => n.Kind == AbsorptionPatternClassifier.AutoSurvivedKind);
        Assert.Contains(result.AdsorptionScoreExclusions, s => s.Contains("is panreactive (4 of 4", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(2, pattern.Matches);
        Assert.Equal(2, pattern.Mismatches);
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("argues against a readily adsorbed autoantibody", doc);
        Assert.Contains("Not a diagnosis", doc);
    }

    [Fact]
    public void RrAdsorption_PanreactiveUntreated_LeavesAntiKOnTheAliquot()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-RR-PAN", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-RR-PAN", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "++");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "K", "+");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-RR-PAN", panelId);
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-RR-PAN", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        foreach (var cell in new[] { "1", "2", "3", "4" })
            iso.Db.SaveReaction(untreated, cell, "0", "0", "2+", "NT");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-RR-PAN", updateDb: false);
        Assert.Contains(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.RemovedKind);
        Assert.Contains(result.AdsorptionScoreExclusions, s =>
            s.Contains("Untreated is panreactive (4 of 4 evaluated cells)", StringComparison.Ordinal) &&
            s.Contains("paired with Absorbed: rr", StringComparison.Ordinal) &&
            s.Contains("not expected to remove", StringComparison.Ordinal) &&
            s.Contains("Not a diagnosis", StringComparison.Ordinal));
        Assert.Contains(result.AdsorptionScoreExclusions, s =>
            s.Contains("remove anti-c;", StringComparison.Ordinal));
        var kPattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-K");
        Assert.Equal(1, kPattern.Matches);
        Assert.Equal(0, kPattern.Mismatches);
        var kEvidence = result.SuspectedEvidence["anti-K"];
        Assert.Single(kEvidence.SupportingCells);
        Assert.Equal("Absorbed: rr", kEvidence.SupportingCells[0].RunLabel);
        Assert.Equal("3", kEvidence.SupportingCells[0].CellNumber);
        Assert.DoesNotContain(result.PatternMatches, p => p.Antibody == "anti-c");
        var antiK = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-K");
        Assert.Contains("is panreactive", antiK.Narrative);
        Assert.Contains("Not a diagnosis", antiK.Narrative);
    }

    [Fact]
    public void RrAdsorption_ThreeOfFourReactive_StillUsesTheUntreatedRun()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-RR-3", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-RR-3", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-RR-3", panelId);
        iso.Db.SaveReaction(untreated, "1", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "2", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "3", "0", "0", "2+", "NT");
        iso.Db.SaveReaction(untreated, "4", "0", "0", "0", "2+");
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-RR-3", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-RR-3", updateDb: false);
        Assert.DoesNotContain(result.AdsorptionScoreExclusions, s => s.Contains("is panreactive", StringComparison.Ordinal));
        var pattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-c");
        Assert.Equal(2, pattern.Matches);
        Assert.Equal(1, pattern.Mismatches);
        Assert.Contains(result.AdsorptionScoreExclusions, s =>
            s.Contains("remove anti-c;", StringComparison.Ordinal));
    }

    [Fact]
    public void RrAdsorption_PanreactiveUntreated_SurvivalIsNotAnOrdinaryAntiCScore()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("ABS-RR-SURV", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("ABS-RR-SURV", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "c", "+");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "c", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "K", "+");
        var untreated = iso.Db.GetOrCreateDefaultRun("ABS-RR-SURV", panelId);
        var adsorbed = iso.Db.AddPanelRun(
            "ABS-RR-SURV", panelId, CellTreatment.None, SerumTreatment.AlloAdsorptionRr, "Absorbed: rr");
        foreach (var cell in new[] { "1", "2", "3", "4" })
            iso.Db.SaveReaction(untreated, cell, "0", "0", "2+", "NT");
        iso.Db.SaveReaction(adsorbed, "1", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "2", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "3", "0", "0", "2+", "2+");
        iso.Db.SaveReaction(adsorbed, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("ABS-RR-SURV", updateDb: false);
        var survived = Assert.Single(result.AbsorptionConclusions, n =>
            n.Antibody == "anti-c" && n.Kind == AbsorptionPatternClassifier.SurvivedKind);
        Assert.Contains("remained reactive", survived.Explanation);
        Assert.Contains("Not a diagnosis", survived.Explanation);
        Assert.DoesNotContain(result.Suspected.Keys, k => k == "anti-c");
        Assert.DoesNotContain(result.PatternMatches, p => p.Antibody == "anti-c");
        var kPattern = Assert.Single(result.PatternMatches, p => p.Antibody == "anti-K");
        Assert.Equal(1, kPattern.Matches);
        Assert.Equal(2, kPattern.Mismatches);
        var antiC = Assert.Single(result.CandidateExplanations, e => e.Antibody == "anti-c");
        Assert.Contains("is panreactive", antiC.Narrative);
        Assert.Contains("remained reactive", antiC.Narrative);
        Assert.Contains("Not a diagnosis", antiC.Narrative);
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
