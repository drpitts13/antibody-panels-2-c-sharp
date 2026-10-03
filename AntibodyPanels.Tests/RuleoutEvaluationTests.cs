using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Tests;

public class RuleoutEvaluationTests
{
    [Fact]
    public void DefaultOneCell_HomozygousE_MeetsAndNamesCell()
    {
        using var iso = new IsolatedDatabase();
        SeedHomozygousNegative(iso, "RO-E-1", "E", "e", extraCells: 1);

        var result = iso.Analyzer.AnalyzeSpecimen("RO-E-1", updateDb: false);
        var ev = RequireEvaluation(result, "anti-E");

        Assert.True(ev.MeetsCriteria);
        Assert.Equal(1, ev.ObservedCount);
        Assert.Equal(1, ev.RequiredCount);
        Assert.Equal("lab default", ev.PolicySource);
        Assert.Contains("cell 1", ev.Explanation);
        Assert.Contains("currently meets configured rule-out criteria", ev.Explanation);
        Assert.Contains(ev.Cells, c => c.CellNumber == "1" && c.IsHomozygous);
        Assert.True(result.RuledOut.ContainsKey("anti-E"));
        Assert.Equal(1, result.RuledOut["anti-E"]);
    }

    [Fact]
    public void AntibodyRuleRequiresThree_OneHomozygousK_IsInProgress()
    {
        using var iso = new IsolatedDatabase();
        SeedHomozygousNegative(iso, "RO-K-3", "K", "k", extraCells: 1);
        iso.Db.AddRule("K needs 3", "Require three homozygous K+ nonreactive cells",
            "anti-K", null, heterozygousOk: false, minRuleoutCount: 3);

        var result = iso.Analyzer.AnalyzeSpecimen("RO-K-3", updateDb: false);
        var ev = RequireEvaluation(result, "anti-K");

        Assert.False(ev.MeetsCriteria);
        Assert.Equal(1, ev.ObservedCount);
        Assert.Equal(3, ev.RequiredCount);
        Assert.Equal("antibody rule", ev.PolicySource);
        Assert.Contains("does not yet meet configured rule-out criteria", ev.Explanation);
        Assert.Contains("1 of 3", ev.Explanation);
        Assert.False(result.RuledOut.ContainsKey("anti-K"));
        Assert.Contains(result.Suggestions, s => s.Contains("1 of 3"));
    }

    [Fact]
    public void TwoHeterozygousE_WithoutHetRule_DoNotCount()
    {
        using var iso = new IsolatedDatabase();
        SeedHeterozygousNegative(iso, "RO-E-HET-NO", cellCount: 2);

        var result = iso.Analyzer.AnalyzeSpecimen("RO-E-HET-NO", updateDb: false);

        Assert.False(result.RuledOut.ContainsKey("anti-E"));
        Assert.DoesNotContain(result.RuleoutEvaluations, e => e.Antibody == "anti-E" && e.MeetsCriteria);
        Assert.True(!result.DetailedRuleouts.ContainsKey("anti-E") ||
                    result.DetailedRuleouts["anti-E"].Count == 0);
    }

    [Fact]
    public void TwoHeterozygousE_WithHetOkRule_MeetsRequiredTwo()
    {
        using var iso = new IsolatedDatabase();
        SeedHeterozygousNegative(iso, "RO-E-HET-OK", cellCount: 2);
        iso.Db.AddRule("E het OK", "Allow heterozygous E rule-out",
            "anti-E", "E", heterozygousOk: true, minRuleoutCount: 2);

        var result = iso.Analyzer.AnalyzeSpecimen("RO-E-HET-OK", updateDb: false);
        var ev = RequireEvaluation(result, "anti-E");

        Assert.True(ev.MeetsCriteria);
        Assert.Equal(2, ev.ObservedCount);
        Assert.Equal(2, ev.RequiredCount);
        Assert.True(ev.HeterozygousAllowed);
        Assert.Equal(2, ev.HeterozygousCount);
        Assert.Contains("heterozygous cells allowed", ev.Explanation);
        Assert.Contains("cells 1, 2", ev.Explanation);
        Assert.True(result.RuledOut.ContainsKey("anti-E"));
    }

    [Fact]
    public void ReactiveEPositiveCell_IsConflict_DoesNotCountAsRuleout()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("RO-E-CONFLICT", "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("RO-E-CONFLICT", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        SetZygosity(iso, cells[0], "E", "e", homozygousPositive: true);
        SetZygosity(iso, cells[1], "E", "e", homozygousPositive: true);
        iso.Db.SaveReaction("RO-E-CONFLICT", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("RO-E-CONFLICT", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("RO-E-CONFLICT", updateDb: false);
        var ev = RequireEvaluation(result, "anti-E");

        Assert.Equal(1, ev.ObservedCount);
        Assert.DoesNotContain(ev.Cells, c => c.CellNumber == "1");
        Assert.Contains(ev.Cells, c => c.CellNumber == "2");
        Assert.Contains("1", ev.ConflictingReactiveCells);
        Assert.DoesNotContain("2", ev.ConflictingReactiveCells);
        Assert.True(ev.MeetsCriteria);
    }

    [Fact]
    public void LabDefaultThree_OneHomozygousE_IsInProgress()
    {
        using var iso = new IsolatedDatabase();
        SeedHomozygousNegative(iso, "RO-E-LAB3", "E", "e", extraCells: 1);
        var previous = AppSettings.Current.DefaultMinRuleoutCount;
        try
        {
            AppSettings.Current.DefaultMinRuleoutCount = 3;
            var result = iso.Analyzer.AnalyzeSpecimen("RO-E-LAB3", updateDb: false);
            var ev = RequireEvaluation(result, "anti-E");
            Assert.False(ev.MeetsCriteria);
            Assert.Equal(3, ev.RequiredCount);
            Assert.Equal("lab default", ev.PolicySource);
            Assert.False(result.RuledOut.ContainsKey("anti-E"));
        }
        finally
        {
            AppSettings.Current.DefaultMinRuleoutCount = previous;
        }
    }

    [Fact]
    public void RuleoutRow_FromEvaluation_ExposesReviewableSentence()
    {
        var ev = new RuleoutEvaluation
        {
            Antibody = "anti-E",
            ObservedCount = 2,
            RequiredCount = 2,
            MeetsCriteria = true,
            Explanation = "Anti-E currently meets configured rule-out criteria because 2 antigen-positive nonreactive cells were observed (required 2; heterozygous cells not allowed).",
        };
        var row = RuleoutRow.From(ev);
        Assert.Equal("Ruled out", row.Status);
        Assert.True(row.MeetsRequired);
        Assert.Equal(2, row.Required);
        Assert.Contains("currently meets configured rule-out criteria", row.Explanation);
    }

    [Fact]
    public void ClinicalReport_IncludesRuleoutExplanation()
    {
        using var iso = new IsolatedDatabase();
        SeedHomozygousNegative(iso, "RO-E-RPT", "E", "e", extraCells: 1);
        var text = iso.Reports.GeneratePreviewText(ReportType.ClinicalIdentification, "RO-E-RPT");
        Assert.Contains("currently meets configured rule-out criteria", text);
        Assert.Contains("Anti-E", text);
    }

    private static RuleoutEvaluation RequireEvaluation(AnalysisResult result, string antibody)
    {
        var ev = result.RuleoutEvaluations.SingleOrDefault(e => e.Antibody == antibody);
        Assert.NotNull(ev);
        return ev!;
    }

    private static void SeedHomozygousNegative(IsolatedDatabase iso, string specimenId,
        string antigen, string antithetical, int extraCells)
    {
        iso.Db.AddSpecimen(specimenId, "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 1 + extraCells, null, false);
        iso.Db.LinkSpecimenPanel(specimenId, panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        SetZygosity(iso, cells[0], antigen, antithetical, homozygousPositive: true);
        iso.Db.SaveReaction(specimenId, panelId, cells[0].CellNumber, "0", "0", "0", "2+");
        for (var i = 1; i < cells.Count; i++)
        {
            iso.Db.UpdatePanelCellAntigen(cells[i].Id, antigen, "-");
            iso.Db.SaveReaction(specimenId, panelId, cells[i].CellNumber, "0", "0", "0", "2+");
        }
    }

    private static void SeedHeterozygousNegative(IsolatedDatabase iso, string specimenId, int cellCount)
    {
        iso.Db.AddSpecimen(specimenId, "serum", null);
        var panelId = iso.Db.AddPanel("P", "L", "V", cellCount, null, false);
        iso.Db.LinkSpecimenPanel(specimenId, panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        foreach (var cell in cells)
        {
            iso.Db.UpdatePanelCellAntigen(cell.Id, "E", "+");
            iso.Db.UpdatePanelCellAntigen(cell.Id, "e", "+");
            iso.Db.SaveReaction(specimenId, panelId, cell.CellNumber, "0", "0", "0", "2+");
        }
    }

    private static void SetZygosity(IsolatedDatabase iso, PanelCell cell,
        string antigen, string antithetical, bool homozygousPositive)
    {
        iso.Db.UpdatePanelCellAntigen(cell.Id, antigen, homozygousPositive ? "+" : "-");
        iso.Db.UpdatePanelCellAntigen(cell.Id, antithetical, homozygousPositive ? "-" : "+");
    }
}
