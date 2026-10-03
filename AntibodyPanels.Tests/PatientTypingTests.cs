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

    [Fact]
    public void Parse_RhceCeAndFy02Pair_PredictsRbcPhenotype()
    {
        var typing = PatientTypingParser.Parse(null, null, null,
            "RHCE*ce/ce; FY*02/FY*02; KEL*02/KEL*02; RHD*01N.01");
        Assert.Equal("-", typing.PredictedAntigens["C"]);
        Assert.Equal("+", typing.PredictedAntigens["c"]);
        Assert.Equal("-", typing.PredictedAntigens["E"]);
        Assert.Equal("+", typing.PredictedAntigens["e"]);
        Assert.Equal("-", typing.PredictedAntigens["Fya"]);
        Assert.Equal("+", typing.PredictedAntigens["Fyb"]);
        Assert.Equal("-", typing.PredictedAntigens["K"]);
        Assert.Equal("+", typing.PredictedAntigens["k"]);
        Assert.Equal("-", typing.PredictedAntigens["D"]);
        Assert.Empty(typing.Antigens);
    }

    [Fact]
    public void Parse_SingleRhceHaplotype_DoesNotInventAntitheticalNegatives()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "RHCE*ce");
        Assert.Equal("+", typing.PredictedAntigens["c"]);
        Assert.Equal("+", typing.PredictedAntigens["e"]);
        Assert.False(typing.PredictedAntigens.ContainsKey("C"));
        Assert.False(typing.PredictedAntigens.ContainsKey("E"));
    }

    [Fact]
    public void Evaluate_TransfusedPhenotype_GenotypeStillPredicted()
    {
        var typing = PatientTypingParser.Parse("E+", null, "Recently transfused 2 RBC units",
            "RHCE*ce/ce");
        Assert.True(typing.PhenotypeUnreliable);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Uninterpretable);
        var predicted = ev.Single(c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Predicted);
        Assert.Equal("-", predicted.PatientValue);
        Assert.Contains("genotype predicts E-", predicted.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Against);
    }

    [Fact]
    public void Evaluate_PhenotypeGenotypeConflict_LeavesBothAsEvidence()
    {
        var typing = PatientTypingParser.Parse("C+", null, null, "RHCE*ce/ce");
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-C" && c.Kind == PatientTypingKind.Against);
        var predicted = ev.Single(c => c.Antibody == "anti-C" && c.Kind == PatientTypingKind.Predicted);
        Assert.Equal("-", predicted.PatientValue);
        Assert.Contains("Serologic typing is C+", predicted.Explanation);
        Assert.Contains("predicts C-", predicted.Explanation);
    }

    [Fact]
    public void Analyzer_TransfusedPlusGenotype_DoesNotAutoRemoveSuspect()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("PHENO-GT", "serum", null, notes: "recently transfused",
            phenotype: "E+", previousAntibodies: null, datResult: null, genotype: "RHCE*ce/ce");
        var stored = iso.Db.GetSpecimen("PHENO-GT");
        Assert.Equal("RHCE*ce/ce", stored!.Genotype);

        var panelId = iso.Db.AddPanel("P", "L", "V", 4, null, false);
        iso.Db.LinkSpecimenPanel("PHENO-GT", panelId);
        var cells = iso.Db.GetPanelCells(panelId);
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[0].Id, "e", "-");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "E", "+");
        iso.Db.UpdatePanelCellAntigen(cells[1].Id, "e", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[2].Id, "e", "+");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "E", "-");
        iso.Db.UpdatePanelCellAntigen(cells[3].Id, "e", "+");
        iso.Db.SaveReaction("PHENO-GT", panelId, "1", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("PHENO-GT", panelId, "2", "0", "0", "3+", "NT");
        iso.Db.SaveReaction("PHENO-GT", panelId, "3", "0", "0", "0", "2+");
        iso.Db.SaveReaction("PHENO-GT", panelId, "4", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("PHENO-GT", updateDb: false);
        Assert.False(result.RuledOut.ContainsKey("anti-E"),
            "Genotype/phenotype evidence must not auto-rule-out anti-E.");
        Assert.True(result.PatientPhenotypeUnreliable);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "-");
        Assert.DoesNotContain(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-E" && c.Kind == PatientTypingKind.Against);
        var exp = result.CandidateExplanations
            .First(e => e.Antibody == "anti-E");
        Assert.Contains("genotype predicts", exp.PhenotypeNote, StringComparison.OrdinalIgnoreCase);
    }
}
