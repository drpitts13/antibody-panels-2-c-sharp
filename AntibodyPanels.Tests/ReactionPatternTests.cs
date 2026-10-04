using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ReactionPatternTests
{
    [Fact]
    public void IsRtPositiveAhgNegative_IsColdPattern_NotADiagnosis()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), isPhase: "2+", ahg: "0")))
            .ToList();

        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Cold && n.MatchingCells == 6);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Panreactive);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Warm);
        Assert.Contains("cold-reactive", notes.First(n => n.Kind == ReactionPatternClassifier.Cold).Explanation);
        Assert.Contains("Not a diagnosis", notes.First(n => n.Kind == ReactionPatternClassifier.Cold).Explanation);
    }

    [Fact]
    public void AhgPositiveIsNegative_IsWarmPattern()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), isPhase: "0", ahg: "2+")))
            .ToList();

        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Warm && n.MatchingCells == 6);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Cold);
    }

    [Fact]
    public void SolidPhasePositive_CountsAsIatWarm()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), isPhase: "0", ahg: "0",
                extra: new Dictionary<string, string> { ["Solid"] = "2+" })))
            .ToList();

        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Warm);
        Assert.Contains("AHG/Gel/Solid", notes.First(n => n.Kind == ReactionPatternClassifier.Warm).Explanation);
    }

    [Fact]
    public void Prewarm_IgnoresIsForColdPattern()
    {
        var ctx = new RunContext(new PanelRun { SerumTreatment = SerumTreatment.Prewarmed });
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(
                Rxn(i.ToString(), isPhase: "2+", ahg: "0"), ctx))
            .ToList();

        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Cold);
    }

    [Fact]
    public void TwoCells_DoNotClassify()
    {
        var cells = new[]
        {
            ReactionPatternClassifier.Observe(Rxn("1", "2+", "0")),
            ReactionPatternClassifier.Observe(Rxn("2", "2+", "0")),
            ReactionPatternClassifier.Observe(Rxn("AC", "2+", "2+"))
        };
        Assert.Empty(ReactionPatternClassifier.Classify(cells));
    }

    [Fact]
    public void WeakIatOnMostCells_IsHtlaPattern()
    {
        var cells = Enumerable.Range(1, 8)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "w+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Htla && n.MatchingCells == 8);
        Assert.Contains("HTLA", notes.First(n => n.Kind == ReactionPatternClassifier.Htla).Explanation);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Warm);
    }

    [Fact]
    public void StrongAhg_IsNotHtla()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "3+")))
            .ToList();
        Assert.DoesNotContain(ReactionPatternClassifier.Classify(cells),
            n => n.Kind == ReactionPatternClassifier.Htla);
    }

    [Fact]
    public void PanreactiveWithNegativeAc_FavorsHighPrevalence()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "2+")))
            .Append(ReactionPatternClassifier.Observe(Rxn("AC", "0", "0")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.HighPrevalence);
        Assert.Contains("high-prevalence", notes.First(n => n.Kind == ReactionPatternClassifier.HighPrevalence).Explanation);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Autoantibody);
    }

    [Fact]
    public void PanreactiveWithPositiveAc_FavorsAutoantibody()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "2+")))
            .Append(ReactionPatternClassifier.Observe(Rxn("AC", "0", "2+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells);
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Autoantibody);
        Assert.Contains("autoantibody", notes.First(n => n.Kind == ReactionPatternClassifier.Autoantibody).Explanation);
    }

    [Fact]
    public void SingleReactiveCell_IsLowFrequencyPattern()
    {
        var cells = Enumerable.Range(1, 8)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", i == 3 ? "2+" : "0")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells);
        var lfa = Assert.Single(notes, n => n.Kind == ReactionPatternClassifier.LowFrequency);
        Assert.Equal(1, lfa.MatchingCells);
        Assert.Contains("low-frequency", lfa.Explanation);
        Assert.Contains("cell 3", lfa.Explanation);
        Assert.Contains("3", lfa.SupportingCells);
        Assert.Contains("evidence score", lfa.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyzer_SurfacesColdPatternOnSuggestions()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PAT-COLD", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 6, null, false);
        iso.Db.LinkSpecimenPanel("PAT-COLD", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        var runId = iso.Db.GetOrCreateDefaultRun("PAT-COLD", panelId);
        foreach (var cell in cells)
        {
            iso.Db.UpdatePanelCellAntigen(cell.Id, "E", "+");
            iso.Db.SaveReaction(runId, cell.CellNumber, "2+", "0", "0", "2+");
        }

        var result = iso.Analyzer.AnalyzeSpecimen("PAT-COLD", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Cold);
        Assert.Contains(result.Suggestions, s => s.Contains("cold-reactive"));
        Assert.DoesNotContain(result.RuledOut.Keys, k => k.Equals("anti-E", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PanreactiveWithOneNegative_ListsConflictingCell()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", i == 6 ? "0" : "2+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells);
        var pan = Assert.Single(notes, n => n.Kind == ReactionPatternClassifier.Panreactive);
        Assert.Contains("6", pan.ConflictingCells);
        Assert.Contains("Nonreactive cell(s): 6", pan.Explanation);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Autoantibody);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.HighPrevalence);
    }

    [Fact]
    public void PanreactiveNoAc_PositiveDat_FavorsAutoantibody()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "2+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells, "2+");
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.Autoantibody);
        var auto = notes.Single(n => n.Kind == ReactionPatternClassifier.Autoantibody);
        Assert.Contains("DAT is 2+", auto.Explanation);
        Assert.Contains("supports autoantibody", auto.Explanation);
        Assert.Contains("Not a diagnosis", auto.Explanation);
        Assert.True(auto.EvidenceScore >= 0.9);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.HighPrevalence);
    }

    [Fact]
    public void PanreactiveNoAc_NegativeDat_FavorsHighPrevalence()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "2+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells, "Negative");
        Assert.Contains(notes, n => n.Kind == ReactionPatternClassifier.HighPrevalence);
        var hfa = notes.Single(n => n.Kind == ReactionPatternClassifier.HighPrevalence);
        Assert.Contains("DAT is negative", hfa.Explanation);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.Autoantibody);
    }

    [Fact]
    public void PanreactivePositiveAc_NegativeDat_IsConflictingAutoEvidence()
    {
        var cells = Enumerable.Range(1, 6)
            .Select(i => ReactionPatternClassifier.Observe(Rxn(i.ToString(), "0", "2+")))
            .Append(ReactionPatternClassifier.Observe(Rxn("AC", "0", "2+")))
            .ToList();
        var notes = ReactionPatternClassifier.Classify(cells, "Negative");
        var auto = notes.Single(n => n.Kind == ReactionPatternClassifier.Autoantibody);
        Assert.Contains("DAT is negative, which argues against", auto.Explanation);
        Assert.Contains("DAT", auto.ConflictingCells);
        Assert.True(auto.EvidenceScore < 1);
        Assert.DoesNotContain(notes, n => n.Kind == ReactionPatternClassifier.HighPrevalence);
    }

    [Fact]
    public void Analyzer_DatPositivePanagglutinin_DoesNotAutoIdentify()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PAT-DAT-AUTO", "serum", null, datResult: "1+");
        var panelId = iso.Db.AddPanel("P", "L", "V", 6, null, false);
        iso.Db.LinkSpecimenPanel("PAT-DAT-AUTO", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        var runId = iso.Db.GetOrCreateDefaultRun("PAT-DAT-AUTO", panelId);
        foreach (var cell in cells)
            iso.Db.SaveReaction(runId, cell.CellNumber, "0", "0", "2+", "NT");

        var result = iso.Analyzer.AnalyzeSpecimen("PAT-DAT-AUTO", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Autoantibody);
        Assert.Contains(result.ReactionPatterns,
            n => n.Kind == ReactionPatternClassifier.Autoantibody && n.Explanation.Contains("DAT is 1+"));
        Assert.Contains(result.Suggestions, s => s.Contains("autoantibody"));
        Assert.DoesNotContain(result.RuledOut.Keys, k => k.StartsWith("anti-", StringComparison.OrdinalIgnoreCase)
            && result.Suspected.ContainsKey(k));
        var doc = AnalysisExplainer.FormatDocument(result);
        Assert.Contains("evidence score", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not a diagnosis", doc);
    }

    private static Reaction Rxn(string cell, string isPhase, string ahg,
        Dictionary<string, string>? extra = null) =>
        new()
        {
            CellNumber = cell,
            IS = isPhase,
            C37 = "0",
            AHG = ahg,
            CC = ahg == "0" ? "2+" : "NT",
            ExtraPhases = extra ?? new Dictionary<string, string>()
        };
}
