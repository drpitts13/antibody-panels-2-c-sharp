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
    public void GataFy01N_DoesNotSupportAlloAntiFya()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "FY*01N.01/FY*02");
        Assert.Equal("-", typing.PredictedAntigens["Fya"]);
        Assert.Equal("+", typing.PredictedAntigens["Fyb"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-Fya" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("GATA", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Fya" && c.Kind == PatientTypingKind.Predicted);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Fya" && c.Kind == PatientTypingKind.Supporting);
    }

    [Fact]
    public void RhdDnb_IsNotTreatedAsDNegative()
    {
        Assert.False(AlleleVariantCatalog.IsRhdNull("DNB"));
        var typing = PatientTypingParser.Parse(null, null, null, "RHD*DNB; RHCE*ce/ce");
        Assert.False(typing.PredictedAntigens.ContainsKey("D"));
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-D" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("partial D", StringComparison.OrdinalIgnoreCase)
                                || c.Explanation.Contains("weak D", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-D" && c.PatientValue == "-");
    }

    [Fact]
    public void WeakD_PredictsDPositiveWithVariantNote()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "RHD*01W.1");
        Assert.Equal("+", typing.PredictedAntigens["D"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-D" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "+");
        Assert.Contains(ev, c => c.Antibody == "anti-D" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains("Not a diagnosis", ev.Single(c => c.Kind == PatientTypingKind.Variant).Explanation);
    }

    [Fact]
    public void RhceCeAr_KeepsEPositive_AndWarnsELikeAlloantibody()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "RHCE*ceAR/ceAR");
        Assert.Equal("+", typing.PredictedAntigens["e"]);
        Assert.Equal("-", typing.PredictedAntigens["E"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-e" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("e-like", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Jk01N_IsTrueNull_AndSupportsPredictedJkaNegative()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "JK*01N.01/JK*02");
        Assert.Equal("-", typing.PredictedAntigens["Jka"]);
        Assert.Equal("+", typing.PredictedAntigens["Jkb"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-Jka" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "-");
        Assert.Contains(ev, c => c.Antibody == "anti-Jka" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("true Jk null", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Jk3");
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Fya" && c.Kind == PatientTypingKind.Variant);
    }

    [Fact]
    public void JkNullDiploid_AddsJk3ReviewNote_WithoutIdentifying()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "JK*01N.01/JK*02N.01");
        Assert.Equal("-", typing.PredictedAntigens["Jka"]);
        Assert.Equal("-", typing.PredictedAntigens["Jkb"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-Jka" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "-");
        Assert.Contains(ev, c => c.Antibody == "anti-Jkb" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "-");
        Assert.Contains(ev, c => c.Antibody == "anti-Jk3" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("Jk(a−b−)", StringComparison.OrdinalIgnoreCase)
                                || c.Explanation.Contains("Jk(a-b-)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not an identification", ev.Single(c => c.Antibody == "anti-Jk3").Explanation,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JkWeak_KeepsPredictedPositive_AndWarnsAlloantibodyPossible()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "JK*01W.01/JK*02");
        Assert.Equal("+", typing.PredictedAntigens["Jka"]);
        Assert.Equal("+", typing.PredictedAntigens["Jkb"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-Jka" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "+");
        Assert.Contains(ev, c => c.Antibody == "anti-Jka" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("does not rule out anti-Jka", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Jk3");
    }

    [Fact]
    public void Kel02NDiploid_PredictsKellNull_WithKuReviewNote()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "KEL*02N.01/KEL*02N.01");
        Assert.Equal("-", typing.PredictedAntigens["K"]);
        Assert.Equal("-", typing.PredictedAntigens["k"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Kind == PatientTypingKind.Variant && c.Explanation.Contains("anti-Ku"));
        Assert.Contains("Not a diagnosis", ev.First(c => c.Kind == PatientTypingKind.Variant).Explanation);
    }

    [Fact]
    public void Gypb03N_AddsUVariantNote_WithoutInventingIdentification()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "GYPB*03N.01/GYPB*04");
        Assert.Equal("-", typing.PredictedAntigens["S"]);
        Assert.Equal("+", typing.PredictedAntigens["s"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-U" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(ev, c => c.Explanation.Contains("U variant", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-U" && c.Kind is PatientTypingKind.Supporting or PatientTypingKind.Predicted);
    }

    [Fact]
    public void Analyzer_JkNullDiploid_DoesNotAutoIdentifyJk3()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("JK-NULL", "serum", null, notes: null, phenotype: null,
            previousAntibodies: null, datResult: null, genotype: "JK*01N.01/JK*02N.01");
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("JK-NULL", panelId);
        iso.Db.SaveReaction("JK-NULL", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("JK-NULL", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("JK-NULL", updateDb: false);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-Jk3" && c.Kind == PatientTypingKind.Variant);
        Assert.False(result.RuledOut.ContainsKey("anti-Jk3"));
        Assert.DoesNotContain(result.Suspected.Keys, k => k.Equals("anti-Jk3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Suggestions, s => s.Contains("identified", StringComparison.OrdinalIgnoreCase)
                                                       && s.Contains("Jk3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gypa01N_DoesNotPredictMPositive()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "GYPA*01N.01/GYPA*02");
        Assert.Equal("-", typing.PredictedAntigens["M"]);
        Assert.Equal("+", typing.PredictedAntigens["N"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-M" && c.Kind == PatientTypingKind.Predicted && c.PatientValue == "-");
        Assert.Contains(ev, c => c.Antibody == "anti-M" && c.Kind == PatientTypingKind.Variant);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Ena");
        Assert.Contains("Not a diagnosis", ev.First(c => c.Kind == PatientTypingKind.Variant).Explanation);
    }

    [Fact]
    public void GypaNullDiploid_PredictsMNNegative_WithEnaReview_NotIdentification()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "GYPA*01N.01/GYPA*02N.01");
        Assert.Equal("-", typing.PredictedAntigens["M"]);
        Assert.Equal("-", typing.PredictedAntigens["N"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.Contains(ev, c => c.Antibody == "anti-Ena" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains("not an identification", ev.Single(c => c.Antibody == "anti-Ena").Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Ena" && c.Kind is PatientTypingKind.Supporting or PatientTypingKind.Predicted);
    }

    [Fact]
    public void GypaOrdinaryMN_DoesNotAddEnaNote()
    {
        var typing = PatientTypingParser.Parse(null, null, null, "GYPA*01/GYPA*02");
        Assert.Equal("+", typing.PredictedAntigens["M"]);
        Assert.Equal("+", typing.PredictedAntigens["N"]);
        var ev = PatientTypingParser.Evaluate(typing);
        Assert.DoesNotContain(ev, c => c.Antibody == "anti-Ena");
        Assert.DoesNotContain(ev, c => c.Kind == PatientTypingKind.Variant);
    }

    [Fact]
    public void Analyzer_GypaMk_DoesNotAutoIdentifyEna()
    {
        using var iso = new IsolatedDatabase();
        iso.Db.AddSpecimen("GYPA-MK", "serum", null, notes: null, phenotype: "M+ N+",
            previousAntibodies: null, datResult: null, genotype: "GYPA*Mk/GYPA*Mk");
        var panelId = iso.Db.AddPanel("P", "L", "V", 2, null, false);
        iso.Db.LinkSpecimenPanel("GYPA-MK", panelId);
        iso.Db.SaveReaction("GYPA-MK", panelId, "1", "0", "0", "0", "2+");
        iso.Db.SaveReaction("GYPA-MK", panelId, "2", "0", "0", "0", "2+");

        var result = iso.Analyzer.AnalyzeSpecimen("GYPA-MK", updateDb: false);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Antibody == "anti-Ena" && c.Kind == PatientTypingKind.Variant);
        Assert.Contains(result.PatientTypingConsiderations,
            c => c.Kind == PatientTypingKind.Predicted && c.Antigen == "M" && c.PatientValue == "-");
        Assert.False(result.RuledOut.ContainsKey("anti-Ena"));
        Assert.DoesNotContain(result.Suspected.Keys, k => k.Equals("anti-Ena", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Suggestions, s => s.Contains("identified", StringComparison.OrdinalIgnoreCase)
                                                       && s.Contains("En", StringComparison.OrdinalIgnoreCase));
    }
}
