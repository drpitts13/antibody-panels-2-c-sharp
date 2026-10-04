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
