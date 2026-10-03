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
