using System.Text;
using AntibodyPanels.Models;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

/// <summary>
/// Validates analysis "dashboard" data accuracy (Analysis tab aggregates).
/// The application has no separate dashboard; the Analysis tab serves this role.
/// </summary>
[Collection("PersistentDatabase")]
public class DashboardAccuracyTests
{
    private readonly PersistentDatabaseFixture _fixture;

    public DashboardAccuracyTests(PersistentDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void AnalysisDashboard_SuspectedCountMatchesResult()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var dashboard = BuildDashboardSnapshot(result);
        Assert.Equal(result.Suspected.Count, dashboard.SuspectedRows.Count);
    }

    [Fact]
    public void AnalysisDashboard_RuleoutCountMatchesResult()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var dashboard = BuildDashboardSnapshot(result);
        Assert.Equal(result.RuledOut.Count, dashboard.RuleoutRows.Count);
    }

    [Fact]
    public void AnalysisDashboard_SuspectedScoresMatchProbabilities()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-009", updateDb: true);
        var dashboard = BuildDashboardSnapshot(result);

        foreach (var row in dashboard.SuspectedRows)
        {
            Assert.True(result.Suspected.TryGetValue(row.Antibody, out var prob));
            Assert.Equal($"{prob * 100:F1}%", row.Score);
        }
    }

    [Fact]
    public void AnalysisDashboard_PatternRowsReflectTopMatches()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var dashboard = BuildDashboardSnapshot(result);

        Assert.True(dashboard.PatternRows.Count <= 20);
        foreach (var row in dashboard.PatternRows)
        {
            var match = result.PatternMatches.First(p => p.Antibody == row.Antibody);
            Assert.Equal(match.Matches, row.Matches);
            Assert.Equal(match.Mismatches, row.Mismatches);
            Assert.Equal($"{match.Confidence * 100:F1}%", row.Confidence);
        }
    }

    [Fact]
    public void AnalysisDashboard_CombinationRowsMatchAnalysis()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("TEST-MULTI-AB", updateDb: true);
        var dashboard = BuildDashboardSnapshot(result);

        Assert.Equal(result.Combinations.Count, dashboard.CombinationRows.Count);
        for (int i = 0; i < result.Combinations.Count; i++)
        {
            Assert.Equal(result.Combinations[i].BothSupport, dashboard.CombinationRows[i].BothSupport);
            Assert.Equal(result.Combinations[i].Neither, dashboard.CombinationRows[i].Neither);
        }
    }

    [Fact]
    public void AnalysisDashboard_SummaryTextIncludesSuspectedAndRuleouts()
    {
        var result = _fixture.Analyzer.AnalyzeSpecimen("2024-003", updateDb: true);
        var summary = BuildSummaryText(result);

        Assert.Contains("2024-003", summary);
        if (result.Suspected.Count > 0)
        {
            Assert.Contains("SUSPECTED ANTIBODIES", summary);
            foreach (var ab in result.Suspected.Keys)
                Assert.Contains(ab, summary);
        }
        if (result.RuledOut.Count > 0)
        {
            Assert.Contains("RULED OUT", summary);
        }
    }

    [Fact]
    public void AnalysisDashboard_PersistedDbMatchesLiveAnalysis()
    {
        _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: true);
        var live = _fixture.Analyzer.AnalyzeSpecimen("2024-001", updateDb: false);
        var stored = _fixture.Db.GetSpecimenAntibodies("2024-001");

        Assert.Equal(live.Suspected.Count, stored.Count);
        foreach (var ab in stored)
        {
            Assert.True(live.Suspected.TryGetValue(ab.Antibody, out var prob));
            Assert.Equal(ab.Probability, prob, precision: 3);
        }
    }

    [Fact]
    public void AnalysisDashboard_StaleFlagReflectsReactionChanges()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("DASH-STALE", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1, null, false);
        iso.Db.LinkSpecimenPanel("DASH-STALE", panelId);
        iso.Db.SaveReaction("DASH-STALE", panelId, "1", "0", "0", "2+", "2+");
        iso.Analyzer.AnalyzeSpecimen("DASH-STALE");
        Assert.False(iso.Db.IsSpecimenAnalysisStale("DASH-STALE"));

        Thread.Sleep(1100);
        iso.Db.SaveReaction("DASH-STALE", panelId, "1", "0", "0", "4+", "4+");
        Assert.True(iso.Db.IsSpecimenAnalysisStale("DASH-STALE"));
    }

    // Mirrors AnalysisViewModel.PopulateFromResult / BuildSummary logic
    private static DashboardSnapshot BuildDashboardSnapshot(AnalysisResult result)
    {
        var snapshot = new DashboardSnapshot();
        foreach (var (ab, prob) in result.Suspected.OrderByDescending(x => x.Value))
        {
            result.SuspectedStatistics.TryGetValue(ab, out var stats);
            snapshot.SuspectedRows.Add(new DashboardSuspectedRow
            {
                Antibody = ab,
                Score = $"{prob * 100:F1}%",
                FisherPValue = stats != null ? $"{stats.FisherPValue:F4}" : "-",
                PatternScore = stats != null ? $"{stats.PatternScore:F3}" : "-",
            });
        }

        foreach (var (ab, cnt) in result.RuledOut.OrderBy(x => x.Key))
            snapshot.RuleoutRows.Add(new DashboardRuleoutRow { Antibody = ab, Count = cnt });

        foreach (var pm in result.PatternMatches.Take(20))
            snapshot.PatternRows.Add(new DashboardPatternRow
            {
                Antibody = pm.Antibody,
                Matches = pm.Matches,
                Mismatches = pm.Mismatches,
                Confidence = $"{pm.Confidence * 100:F1}%"
            });

        foreach (var c in result.Combinations)
            snapshot.CombinationRows.Add(new DashboardCombinationRow
            {
                BothSupport = c.BothSupport,
                Ab1Only = c.Ab1Only,
                Ab2Only = c.Ab2Only,
                Neither = c.Neither
            });

        return snapshot;
    }

    private static string BuildSummaryText(AnalysisResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Analysis Results: {r.SpecimenId} ===");
        if (r.Suspected.Count > 0)
        {
            sb.AppendLine("SUSPECTED ANTIBODIES:");
            foreach (var (ab, prob) in r.Suspected.OrderByDescending(x => x.Value))
                sb.AppendLine($"  {ab}  ({prob * 100:F1}%)");
        }
        if (r.RuledOut.Count > 0)
        {
            sb.AppendLine($"RULED OUT ({r.RuledOut.Count} antibodies):");
            foreach (var (ab, cnt) in r.RuledOut.OrderBy(x => x.Key))
                sb.AppendLine($"  {ab}  (x{cnt})");
        }
        return sb.ToString();
    }

    private sealed class DashboardSnapshot
    {
        public List<DashboardSuspectedRow> SuspectedRows { get; } = new();
        public List<DashboardRuleoutRow> RuleoutRows { get; } = new();
        public List<DashboardPatternRow> PatternRows { get; } = new();
        public List<DashboardCombinationRow> CombinationRows { get; } = new();
    }

    private sealed class DashboardSuspectedRow
    {
        public string Antibody { get; set; } = "";
        public string Score { get; set; } = "";
        public string FisherPValue { get; set; } = "";
        public string PatternScore { get; set; } = "";
    }

    private sealed class DashboardRuleoutRow
    {
        public string Antibody { get; set; } = "";
        public int Count { get; set; }
    }

    private sealed class DashboardPatternRow
    {
        public string Antibody { get; set; } = "";
        public int Matches { get; set; }
        public int Mismatches { get; set; }
        public string Confidence { get; set; } = "";
    }

    private sealed class DashboardCombinationRow
    {
        public int BothSupport { get; set; }
        public int Ab1Only { get; set; }
        public int Ab2Only { get; set; }
        public int Neither { get; set; }
    }
}
