using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

public class AcsEvaluationTests
{
    private static readonly (string First, string Second)[] CsPairs =
    {
        ("C", "c"), ("E", "e"), ("K", "k"),
        ("Fya", "Fyb"), ("Jka", "Jkb"), ("S", "s"),
        ("Lea", "Leb"), ("M", "N")
    };

    [Fact]
    public void EvaluateAcs_AllRuledOut_NoHighScore_IsEligible()
    {
        var ruledOut = AllCsRuledOut(3);
        var acs = AntibodyAnalyzer.EvaluateAcs(ruledOut, new Dictionary<string, double>());
        Assert.True(acs.IsEligible);
        Assert.False(acs.IsEligibleWithException);
        Assert.Empty(acs.Shortfalls);
        Assert.Equal(AntigenConstants.AcsResultText, acs.SuggestedCombinedResult);
        Assert.Equal("", acs.SuggestedComment);
    }

    [Fact]
    public void EvaluateAcs_AllRuledOut_HighScore_IsException()
    {
        var ruledOut = AllCsRuledOut(3);
        var scores = new Dictionary<string, double> { ["anti-E"] = 0.96 };
        var acs = AntibodyAnalyzer.EvaluateAcs(ruledOut, scores);
        Assert.False(acs.IsEligible);
        Assert.True(acs.IsEligibleWithException);
        Assert.Equal($"{AntigenConstants.AcsResultText}; anti-E", acs.SuggestedCombinedResult);
        Assert.Equal("anti-E ruled out 3 times", acs.SuggestedComment);
    }

    [Fact]
    public void EvaluateAcs_Shortfall_IsNotEligible()
    {
        var ruledOut = AllCsRuledOut(3);
        ruledOut["anti-K"] = 2;
        var acs = AntibodyAnalyzer.EvaluateAcs(ruledOut, new Dictionary<string, double>());
        Assert.False(acs.IsEligible);
        Assert.False(acs.IsEligibleWithException);
        Assert.Contains(acs.Shortfalls, s => s.Antibody == "anti-K" && s.Count == 2 && s.Required == 3);
    }

    [Fact]
    public void EvaluateAcs_UsesAcsRuleoutCountSetting()
    {
        var previous = AppSettings.Current.AcsRuleoutCount;
        try
        {
            AppSettings.Current.AcsRuleoutCount = 2;
            var ruledOut = AllCsRuledOut(2);
            var acs = AntibodyAnalyzer.EvaluateAcs(ruledOut, new Dictionary<string, double>());
            Assert.True(acs.IsEligible);
            Assert.Equal(2, acs.RequiredRuleoutCount);
        }
        finally
        {
            AppSettings.Current.AcsRuleoutCount = previous;
        }
    }

    [Fact]
    public void AnalyzeSpecimen_AllNegativeCsPanel_IsAcsEligible()
    {
        var previous = AppSettings.Current.AcsRuleoutCount;
        try
        {
            AppSettings.Current.AcsRuleoutCount = 3;
            using var iso = new IsolatedDatabase();
            BuildAllNegativeCsPanel(iso, "ACS-NEG", extraReactiveECells: 0, kPositiveOnFirstCell: true);
            var result = iso.Analyzer.AnalyzeSpecimen("ACS-NEG", updateDb: false);
            Assert.True(result.Acs.IsEligible, ShortfallMessage(result));
            Assert.False(result.Acs.IsEligibleWithException);
            Assert.Empty(result.Suspected);
            Assert.Equal(AntigenConstants.AcsResultText,
                AnalysisViewModel.SuggestedFinalId(Array.Empty<SuspectedRow>(), result.Acs));
            Assert.Contains(result.Suggestions, s => s.Contains(AntigenConstants.AcsResultText));

            var text = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "ACS-NEG");
            Assert.Contains(AntigenConstants.AcsResultText, text);
        }
        finally
        {
            AppSettings.Current.AcsRuleoutCount = previous;
        }
    }

    [Fact]
    public void AnalyzeSpecimen_HighScoreWithCsRuleouts_IsAcsException()
    {
        var previous = AppSettings.Current.AcsRuleoutCount;
        try
        {
            AppSettings.Current.AcsRuleoutCount = 3;
            using var iso = new IsolatedDatabase();
            BuildAllNegativeCsPanel(iso, "ACS-EXC", extraReactiveECells: 6, kPositiveOnFirstCell: true);
            var result = iso.Analyzer.AnalyzeSpecimen("ACS-EXC", updateDb: false);
            Assert.False(result.Acs.IsEligible, ShortfallMessage(result));
            Assert.True(result.Acs.IsEligibleWithException, ScoreMessage(result));
            Assert.Contains(result.Acs.Exceptions, e => e.Antibody == "anti-E" && e.CombinedScore >= 0.95);
            Assert.Contains("anti-E", result.Acs.SuggestedCombinedResult);
            Assert.Contains("anti-E ruled out", result.Acs.SuggestedComment);
        }
        finally
        {
            AppSettings.Current.AcsRuleoutCount = previous;
        }
    }

    [Fact]
    public void AnalyzeSpecimen_MissingCsRuleout_IsNotAcs()
    {
        var previous = AppSettings.Current.AcsRuleoutCount;
        try
        {
            AppSettings.Current.AcsRuleoutCount = 3;
            using var iso = new IsolatedDatabase();
            BuildAllNegativeCsPanel(iso, "ACS-SHORT", extraReactiveECells: 0, kPositiveOnFirstCell: false);
            var result = iso.Analyzer.AnalyzeSpecimen("ACS-SHORT", updateDb: false);
            Assert.False(result.Acs.IsEligible);
            Assert.False(result.Acs.IsEligibleWithException);
            Assert.Contains(result.Acs.Shortfalls, s => s.Antibody == "anti-K");
            Assert.Contains(result.Suggestions, s => s.StartsWith("ACS not met:"));
        }
        finally
        {
            AppSettings.Current.AcsRuleoutCount = previous;
        }
    }

    private static Dictionary<string, int> AllCsRuledOut(int count) =>
        AntigenConstants.ClinicallySignificantAntigens.ToDictionary(ag => $"anti-{ag}", _ => count);

    private static void BuildAllNegativeCsPanel(
        IsolatedDatabase iso, string accession, int extraReactiveECells, bool kPositiveOnFirstCell)
    {
        var numCells = 6 + extraReactiveECells;
        iso.Db.AddSpecimen(accession, "serum", null);
        var panelId = iso.Db.AddPanel("ACS Panel", "LOT-ACS", "Test", numCells, null, false);
        iso.Db.LinkSpecimenPanel(accession, panelId);

        var cells = iso.Db.GetPanelCells(panelId);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            foreach (var ag in AntigenConstants.Antigens)
                cell.SetAntigen(ag, "-");

            if (i < 3)
            {
                cell.SetAntigen("D", "+");
                foreach (var (first, second) in CsPairs)
                {
                    cell.SetAntigen(first, "+");
                    cell.SetAntigen(second, "-");
                }
                if (!kPositiveOnFirstCell && i == 0)
                    cell.SetAntigen("K", "-");
            }
            else if (i < 6)
            {
                cell.SetAntigen("D", "+");
                foreach (var (first, second) in CsPairs)
                {
                    cell.SetAntigen(first, "-");
                    cell.SetAntigen(second, "+");
                }
            }
            else
            {
                cell.SetAntigen("E", "+");
                cell.SetAntigen("e", "-");
            }

            iso.Db.UpdatePanelCell(cell);
        }

        foreach (var cell in cells)
        {
            var reactive = extraReactiveECells > 0 && int.Parse(cell.CellNumber) > 6;
            if (reactive)
                iso.Db.SaveReaction(accession, panelId, cell.CellNumber, "0", "0", "3+", "3+");
            else
                iso.Db.SaveReaction(accession, panelId, cell.CellNumber, "0", "0", "0", "2+");
        }
    }

    private static string ShortfallMessage(AnalysisResult result) =>
        "Shortfalls: " + string.Join(", ",
            result.Acs.Shortfalls.Select(s => $"{s.Antibody} {s.Count}/{s.Required}"));

    private static string ScoreMessage(AnalysisResult result) =>
        "Exceptions: " + string.Join(", ",
            result.Acs.Exceptions.Select(e => $"{e.Antibody} {e.CombinedScore:0.000}")) +
        "; scores expected anti-E >= 0.95";
}
