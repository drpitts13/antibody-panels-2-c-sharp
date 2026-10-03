using AntibodyPanels.Models;
using AntibodyPanels.Services;
using AntibodyPanels.Tests.Infrastructure;

namespace AntibodyPanels.Tests;

public class PatientTypingTests
{
    [Fact]
    public void Parse_R1rAndFyaPair_SetsRhAndDuffy()
    {
        var typing = PatientTypingParser.Parse("R1r, Fy(a-b+), K-", null, null);
        Assert.Equal("+", typing.Antigens["D"]);
        Assert.Equal("+", typing.Antigens["C"]);
        Assert.Equal("+", typing.Antigens["c"]);
        Assert.Equal("-", typing.Antigens["E"]);
        Assert.Equal("+", typing.Antigens["e"]);
        Assert.Equal("-", typing.Antigens["Fya"]);
        Assert.Equal("+", typing.Antigens["Fyb"]);
        Assert.Equal("-", typing.Antigens["K"]);
    }

    [Fact]
    public void Parse_rr_IsDNegative()
    {
        var typing = PatientTypingParser.Parse("rr", null, null);
        Assert.Equal("-", typing.Antigens["D"]);
        Assert.Equal("-", typing.Antigens["C"]);
        Assert.Equal("+", typing.Antigens["c"]);
        Assert.Equal("-", typing.Antigens["E"]);
        Assert.Equal("+", typing.Antigens["e"]);
    }

    [Fact]
    public void Evaluate_PatientEPositive_ArguesAgainstAlloAntiE()
    {
        var typing = PatientTypingParser.Parse("E+", null, null);
        var ev = PatientTypingParser.Evaluate(typing);
        var antiE = ev.Single(c => c.Antibody == "anti-E");
        Assert.Equal(PatientTypingKind.Against, antiE.Kind);
        Assert.Contains("argues against", antiE.Explanation);
    }

    [Fact]
    public void Evaluate_RecentTransfusion_MakesPhenotypeUninterpretable()
    {
        var typing = PatientTypingParser.Parse("E+", null, "Recently transfused 2 RBC units");
        Assert.True(typing.PhenotypeUnreliable);
        var ev = PatientTypingParser.Evaluate(typing);
        var antiE = ev.Single(c => c.Antibody == "anti-E");
        Assert.Equal(PatientTypingKind.Uninterpretable, antiE.Kind);
        Assert.Contains("cannot", antiE.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Against);
    }

    [Fact]
    public void Evaluate_PreviousAntibody_IsHistoricalEvidence()
    {
        var typing = PatientTypingParser.Parse(null, "anti-K; anti-E", null);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-K" && c.Kind == PatientTypingKind.Historical);
        Assert.Contains(ev, c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Historical);
    }

    [Fact]
    public void Analyzer_ConflictingPhenotype_WarnsButDoesNotRemoveSuspect()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHENO-E", "serum", null, notes: null, phenotype: "R2R2",
            previousAntibodies: null);
        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("PHENO-E", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "e", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "e", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "e", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "e", "+");
        iso.Db.SaveReaction("PHENO-E", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("PHENO-E", panelId, "2", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("PHENO-E", panelId, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction("PHENO-E", panelId, "4", "0", "0", "0", "2+");

        var previous = AppSettings.Current.IdentificationCellCount;
        try
        {
            AppSettings.Current.IdentificationCellCount = 2;
            var result = iso.Analyzer.AnalyzeSpecimen("PHENO-E", updateDb: false);
            Assert.True(result.Suspected.ContainsKey("anti-E"),
                $"Expected anti-E still suspected. Found: {string.Join(", ", result.Suspected.Keys)}");
            Assert.Contains(result.PatientTypingConsiderations,
                c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Against);
            Assert.Contains(result.Suggestions, s => s.Contains("argues against"));
        }
        finally
        {
            AppSettings.Current.IdentificationCellCount = previous;
        }
    }

    [Fact]
    public void Analyzer_TransfusedNote_DoesNotArgueAgainst()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHENO-TX", "serum", null, notes: "recently transfused",
            phenotype: "E+", previousAntibodies: "anti-K");
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("PHENO-TX", panelId);
        iso.Db.SaveReaction("PHENO-TX", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("PHENO-TX", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("PHENO-TX", updateDb: false);
        Assert.True(result.PatientPhenotypeUnreliable);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Uninterpretable);
        Assert.DoesNotContain(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Against);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-K" && c.Kind == PatientTypingKind.Historical);
    }
}
