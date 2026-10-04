using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class ExtraPhaseTests
{
    [Fact]
    public void Parse_IgnoresReservedAndDuplicates()
    {
        var parsed = ExtraPhaseParser.Parse("RT, AHG, peg, RT, Gel");
        Assert.Equal(new[] { "RT", "peg", "Gel" }, parsed);
    }

    [Fact]
    public void Parse_AllowsTiterGridNames_AndDoesNotSplitReciprocals()
    {
        var parsed = ExtraPhaseParser.Parse("Dil1, Dil2, Dil4, 1:8, 1:16, Gel");
        Assert.Contains("Dil1", parsed);
        Assert.Contains("Dil4", parsed);
        Assert.Contains("1:8", parsed);
        Assert.Contains("1:16", parsed);
        Assert.Contains("Gel", parsed);
        Assert.DoesNotContain("8", parsed);
        Assert.True(ExtraPhaseParser.IsDilution("1:8"));
        Assert.False(ExtraPhaseParser.IsIatLike("1:8"));
        Assert.Empty(ExtraPhaseParser.Parse(""));
    }

    [Fact]
    public void EmptyConfigured_DoesNotInventSuggestedColumns()
    {
        Assert.Empty(ExtraPhaseParser.Parse(""));
        Assert.Equal("", ExtraPhaseParser.FromSuggested(false, false, false, false, ""));
        Assert.Equal("", ExtraPhaseParser.NormalizeList(null));
        var settings = LabSettings.CreateDefault();
        settings.Clamp();
        Assert.Equal("", settings.ExtraPhases);
    }

    [Fact]
    public void FromSuggested_SolidOnly_IsIatLikeAndKeepsCustom()
    {
        Assert.Equal("Solid", ExtraPhaseParser.FromSuggested(false, false, false, true, ""));
        Assert.Equal("RT, Gel, Albumin", ExtraPhaseParser.FromSuggested(true, false, true, false, "RT, Albumin"));
        Assert.True(ExtraPhaseParser.IsIatLike("Solid"));
        Assert.True(ExtraPhaseParser.IsIatLike("Gel"));
        Assert.True(ExtraPhaseParser.IsIatLike("PEG"));
        Assert.False(ExtraPhaseParser.IsIatLike("RT"));
        Assert.False(ExtraPhaseParser.Contains("", "Solid"));
        Assert.True(ExtraPhaseParser.Contains("solid, gel", "Solid"));
    }

    [Fact]
    public void Toggle_AddsCanonicalSuggested_AndRefusesReserved()
    {
        Assert.Equal("Gel", ExtraPhaseParser.Toggle("", "gel", true));
        Assert.Equal("PEG", ExtraPhaseParser.Toggle("RT, PEG", "rt", false));
        Assert.Equal("", ExtraPhaseParser.Toggle("", "AHG", true));
        Assert.Equal("RT, PEG", ExtraPhaseParser.Toggle("RT, PEG", "37C", true));
        Assert.Equal("RT, LISS", ExtraPhaseParser.Toggle("RT, LISS", "Solid", false));
    }

    [Fact]
    public void SolidOnlyLabDefault_StillLeavesAhgRuleOutWhenSolidIsZero()
    {
        using var iso = new IsolatedDatabase();
        var previous = AppSettings.Current.ExtraPhases;
        try
        {
            AppSettings.Current.ExtraPhases = ExtraPhaseParser.FromSuggested(false, false, false, true);
            Assert.Equal("Solid", AppSettings.Current.ExtraPhases);
            SeedECell(iso, "XP-SOLID0", extra: new Dictionary<string, string> { ["Solid"] = "0" },
                ahg: "0", extraNegative: true);
            var result = iso.Analyzer.AnalyzeSpecimen("XP-SOLID0", updateDb: false);
            Assert.True(result.RuledOut.ContainsKey("anti-E"));
            Assert.DoesNotContain(result.ReactionPatterns, n => n.Kind == ReactionPatternClassifier.Warm);
        }
        finally
        {
            AppSettings.Current.ExtraPhases = previous;
        }
    }

    [Fact]
    public void RtTwoPlus_AhgZero_IsPositive_AndDoesNotRuleOut()
    {
        using var iso = new IsolatedDatabase();
        var previous = AppSettings.Current.ExtraPhases;
        try
        {
            AppSettings.Current.ExtraPhases = "RT, PEG";
            SeedECell(iso, "XP-RT", extra: new Dictionary<string, string> { ["RT"] = "2+" }, ahg: "0");

            var loaded = iso.Db.GetReactions("XP-RT", iso.Db.GetAllPanels()[0].PanelId);
            Assert.Equal("2+", loaded[0].ExtraPhases["RT"]);

            var ctx = new RunContext(new PanelRun());
            Assert.True(ctx.IsPositive(loaded[0]));
            Assert.False(ctx.IsNegative(loaded[0]));
            var (phase, value) = ctx.GetStrongestPhase(loaded[0]);
            Assert.Equal("RT", phase);
            Assert.Equal("2+", value);

            var result = iso.Analyzer.AnalyzeSpecimen("XP-RT", updateDb: false);
            Assert.False(result.RuledOut.ContainsKey("anti-E"));
            Assert.Contains(result.SpecialReactionNotes,
                n => n.Contains("RT") && n.Contains("phase-specific"));
            Assert.Contains(result.Suggestions, s => s.Contains("RT"));
        }
        finally
        {
            AppSettings.Current.ExtraPhases = previous;
        }
    }

    [Fact]
    public void ExtraPhaseZero_AhgZero_StillRulesOut()
    {
        using var iso = new IsolatedDatabase();
        var previous = AppSettings.Current.ExtraPhases;
        try
        {
            AppSettings.Current.ExtraPhases = "RT";
            SeedECell(iso, "XP-0", extra: new Dictionary<string, string> { ["RT"] = "0" }, ahg: "0",
                extraNegative: true);
            var result = iso.Analyzer.AnalyzeSpecimen("XP-0", updateDb: false);
            Assert.True(result.RuledOut.ContainsKey("anti-E"));
        }
        finally
        {
            AppSettings.Current.ExtraPhases = previous;
        }
    }

    [Fact]
    public void Prewarm_TreatsRtLikeIs()
    {
        var ctx = new RunContext(new PanelRun { SerumTreatment = SerumTreatment.Prewarmed });
        Assert.False(ctx.IsPhaseInterpretable("IS"));
        Assert.False(ctx.IsPhaseInterpretable("RT"));
        Assert.True(ctx.IsPhaseInterpretable("AHG"));
        Assert.True(ctx.IsPhaseInterpretable("PEG"));
    }

    [Fact]
    public void PersistRoundTrip_KeepsExtraPhase()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("XP-SAVE", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("XP-SAVE", panelId);
        var runId = iso.Db.GetOrCreateDefaultRun("XP-SAVE", panelId);
        iso.Db.SaveReaction(runId, "1", "0", "0", "0", "2+",
            new Dictionary<string, string> { ["PEG"] = "1+" });
        var loaded = iso.Db.GetReactions(runId).Single();
        Assert.Equal("1+", loaded.ExtraPhases["PEG"]);
        Assert.Equal("0", loaded.AHG);
    }

    private static void SeedECell(IsolatedDatabase iso, string specimenId,
        Dictionary<string, string> extra, string ahg, bool extraNegative = false)
    {
        iso.Db.AddSpecimen(specimenId, "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", extraNegative ? 2 : 1, null, false);
        iso.Db.LinkSpecimenPanel(specimenId, panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "e", "-");
        var runId = iso.Db.GetOrCreateDefaultRun(specimenId, panelId);
        iso.Db.SaveReaction(runId, "1", "0", "0", ahg, ahg == "0" ? "2+" : "NT", extra);
        if (extraNegative)
        {
            iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "-");
            iso.Db.SaveReaction(runId, "2", "0", "0", "0", "2+");
        }
    }
}
