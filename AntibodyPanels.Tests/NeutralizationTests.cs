using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class NeutralizationTests
{
    [Fact]
    public void ParseSubstance_MapsPlasmaUrineAndP1()
    {
        Assert.Equal(NeutralizationSubstance.Plasma,
            NeutralizationParser.ParseSubstance("neutralized with pooled plasma"));
        Assert.Equal(NeutralizationSubstance.Urine,
            NeutralizationParser.ParseSubstance("inhibited with urine / Sda"));
        Assert.Equal(NeutralizationSubstance.P1,
            NeutralizationParser.ParseSubstance("P1 substance neutralization"));
        Assert.True(NeutralizationParser.MentionsNeutralization("reactivity inhibited by C4"));
        Assert.False(NeutralizationParser.MentionsNeutralization("titer 64 only"));
    }

    [Fact]
    public void IatLostAfterNeut_FavorsSolubleSubstance_NotADiagnosis()
    {
        var result = new AnalysisResult();
        var rxns = Enumerable.Range(1, 6).Select(i => Rxn(i.ToString(), "1+", "0"));
        NeutralizationNotes.Apply(result, "neutralized with plasma", rxns);

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == NeutralizationNotes.Kind);
        Assert.Equal(6, note.MatchingCells);
        Assert.Contains("became nonreactive", note.Explanation);
        Assert.Contains("plasma", note.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not a diagnosis", note.Explanation);
    }

    [Fact]
    public void IatPersistsAfterNeut_ArguesAgainstChRg()
    {
        var result = new AnalysisResult();
        var rxns = Enumerable.Range(1, 6).Select(i => Rxn(i.ToString(), "2+", "2+"));
        NeutralizationNotes.Apply(result, "urine neutralization", rxns);

        var note = Assert.Single(result.ReactionPatterns, n => n.Kind == NeutralizationNotes.Kind);
        Assert.Contains("remained reactive", note.Explanation);
        Assert.Contains("urine", note.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("favors a soluble-substance", note.Explanation);
    }

    [Fact]
    public void NotesOnly_IsQualitative()
    {
        var result = new AnalysisResult();
        NeutralizationNotes.Apply(result, "neutralized with plasma", Array.Empty<Reaction>());
        var note = Assert.Single(result.ReactionPatterns);
        Assert.Contains("qualitative note only", note.Explanation);
        Assert.Equal(0, note.MatchingCells);
    }

    [Fact]
    public void TwoPairedCells_DoNotClassify()
    {
        var result = new AnalysisResult();
        NeutralizationNotes.Apply(result, null, new[]
        {
            Rxn("1", "1+", "0"),
            Rxn("2", "1+", "0")
        });
        Assert.Empty(result.ReactionPatterns);
    }

    [Fact]
    public void Analyzer_HtlaPlusNeutralization_DoesNotAutoIdentify()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("NEUT-HTLA", "serum", null, notes: "neutralized with plasma; titer 64");
        var panelId = iso.Db.AddPanel("P", "L", "V", 8, null, false);
        iso.Db.LinkSpecimenPanel("NEUT-HTLA", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        var runId = iso.Db.GetOrCreateDefaultRun("NEUT-HTLA", panelId);
        foreach (var cell in cells)
        {
            iso.Db.SaveReaction(runId, cell.CellNumber, "0", "0", "w+", "NT",
                new Dictionary<string, string> { ["Neut"] = "0" });
        }

        var result = iso.Analyzer.AnalyzeSpecimen("NEUT-HTLA", updateDb: false);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Htla);
        Assert.Contains(result.ReactionPatterns, n => n.Kind == NeutralizationNotes.Kind);
        Assert.Contains(result.ReactionPatterns,
            n => n.Kind == ReactionPatternClassifier.Htla &&
                 n.Explanation.Contains("Neutralization of IAT"));
        Assert.DoesNotContain(result.RuledOut.Keys, k => k.Equals("anti-Ch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Suggestions, s => s.Contains("soluble-substance") || s.Contains("neutral", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void P1Neutralization_RecommendsUnusedSelectedP1Negative_NotOrdinaryPanelP1Neg()
    {
        var result = new AnalysisResult();
        var rxns = Enumerable.Range(1, 6).Select(i => Rxn(i.ToString(), "1+", "0"));
        NeutralizationNotes.Apply(result, "neutralized with P1 substance", rxns);
        Assert.True(NeutralizationParser.FavorsSolubleSubstanceFollowUp(result));

        var selectogen = new Panel { PanelId = 2, Name = "Selectogen", LotNumber = "S" };
        var p1neg = new PanelCell { CellNumber = "8" };
        p1neg.SetAntigen("P1", "-");
        var ordinary = new Panel { PanelId = 3, Name = "ID panel", LotNumber = "I" };
        var ordinaryP1 = new PanelCell { CellNumber = "9" };
        ordinaryP1.SetAntigen("P1", "-");
        ordinaryP1.SetAntigen("E", "+");

        var recs = SelectedCellRecommender.Recommend(
            result,
            Array.Empty<(int, string)>(),
            new[] { (ordinary, ordinaryP1), (selectogen, p1neg) });

        Assert.Contains(recs, r => r.CellNumber == "8");
        var rec = recs.First(r => r.CellNumber == "8");
        Assert.Contains("P1", rec.Explanation);
        Assert.Contains("neutralization", rec.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(recs, r => r.CellNumber == "9");
        Assert.DoesNotContain(recs, r => r.Explanation.Contains("identified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PersistedIatAfterUrineNeut_DoesNotRecommendSdaNegativeCell()
    {
        var result = new AnalysisResult();
        var rxns = Enumerable.Range(1, 6).Select(i => Rxn(i.ToString(), "2+", "2+"));
        NeutralizationNotes.Apply(result, "urine neutralization", rxns);
        Assert.False(NeutralizationParser.FavorsSolubleSubstanceFollowUp(result));

        var stock = new Panel { PanelId = 2, Name = "Selectogen rare" };
        var sda = new PanelCell { CellNumber = "4", SpecialTypes = "Sd(a-)" };
        var recs = SelectedCellRecommender.Recommend(
            result, Array.Empty<(int, string)>(), new[] { (stock, sda) });
        Assert.Empty(recs);
    }

    [Fact]
    public void QualitativePlasmaNote_RecommendsChNegativeSelectedCell()
    {
        var result = new AnalysisResult();
        NeutralizationNotes.Apply(result, "neutralized with plasma", Array.Empty<Reaction>());
        var stock = new Panel { PanelId = 2, Name = "0.8% selected cells" };
        var ch = new PanelCell { CellNumber = "3", SpecialTypes = "Ch-" };
        var recs = SelectedCellRecommender.Recommend(
            result, Array.Empty<(int, string)>(), new[] { (stock, ch) });
        Assert.Contains(recs, r => r.CellNumber == "3" && r.Explanation.Contains("Ch"));
    }

    private static Reaction Rxn(string cell, string ahg, string neut) => new()
    {
        CellNumber = cell,
        IS = "0",
        C37 = "0",
        AHG = ahg,
        CC = "NT",
        ExtraPhases = { ["Neut"] = neut }
    };
}
