using System;
using System.IO;
using System.Linq;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;
using Xunit;

namespace AntibodyPanels.Tests
{
    /// <summary>
    /// Integration tests for the seven real-world serology scenarios.
    /// Each test uses an in-memory SQLite file, seeds data, runs analysis,
    /// and asserts the expected immunological conclusion.
    /// </summary>
    public class ScenarioTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseService _db;
        private readonly AntibodyAnalyzer _analyzer;

        public ScenarioTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
            _db = new DatabaseService(_dbPath);
            _analyzer = new AntibodyAnalyzer(_db);
            DemoDataSeeder.Seed(_db);
        }

        public void Dispose()
        {
            _db.Dispose();
            try { File.Delete(_dbPath); } catch { }
        }

        // ── Scenario 1 — Simple anti-E ────────────────────────────────────────

        [Fact]
        public void Scenario1_AntiE_IsSuspected()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario1Id);
            Assert.Contains("anti-E", result.Suspected.Keys);
        }

        [Fact]
        public void Scenario1_AntiE_ProbabilityHigh()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario1Id);
            Assert.True(result.Suspected["anti-E"] > 0.8,
                $"anti-E probability should be > 0.8, was {result.Suspected["anti-E"]}");
        }

        [Fact]
        public void Scenario1_AntiE_IsNotRuledOut()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario1Id);
            Assert.DoesNotContain("anti-E", result.RuledOut.Keys);
        }

        [Fact]
        public void Scenario1_AntiE_NoGatedRuleoutsForE()
        {
            // Untreated panel — no treatment, so no gating should occur for anti-E
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario1Id);
            Assert.DoesNotContain(result.GatedRuleouts, g => g.Antibody == "anti-E");
        }

        // ── Scenario 2 — Ficin resolves anti-Fya ─────────────────────────────

        [Fact]
        public void Scenario2_AntiFya_IsSuspectedFromUntreatedRun()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario2Id);
            // Fya+ cells were reactive on the untreated run
            Assert.Contains("anti-Fya", result.Suspected.Keys);
        }

        [Fact]
        public void Scenario2_Ficin_GatesFyaRuleout()
        {
            // The ficin run should trigger a gated rule-out for anti-Fya
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario2Id);
            Assert.Contains(result.GatedRuleouts, g => g.Antibody == "anti-Fya");
        }

        [Fact]
        public void Scenario2_Ficin_TreatmentBannerAntigens()
        {
            // AntigenTreatmentEffects lookup should flag Fya as destroyed by ficin
            var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, "Fya");
            Assert.Equal(AntigenEffect.Destroyed, effect);
        }

        [Fact]
        public void Scenario2_Ficin_DoesNotGateDForRuleout()
        {
            // D is enhanced by ficin — it is NOT destroyed, so no gating for anti-D
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario2Id);
            Assert.DoesNotContain(result.GatedRuleouts, g => g.Antibody == "anti-D");
        }

        // ── Scenario 3 — Cold anti-M (IgM, IS-only) ──────────────────────────

        [Fact]
        public void Scenario3_AntiM_IsSuspectedAtISPhase()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario3Id);
            // anti-M should appear as a phase-specific IS probability
            Assert.Contains("anti-M", result.Suspected.Keys.Union(
                result.PhraseProbabilities.ContainsKey("IS")
                    ? result.PhraseProbabilities["IS"].Keys
                    : Array.Empty<string>()));
        }

        [Fact]
        public void Scenario3_Ficin_GatesMRuleout()
        {
            // M is destroyed by ficin — ficin run should generate gated rule-out for anti-M
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario3Id);
            Assert.Contains(result.GatedRuleouts, g => g.Antibody == "anti-M");
        }

        [Fact]
        public void Scenario3_Ficin_DestroysMOnCells()
        {
            var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, "M");
            Assert.Equal(AntigenEffect.Destroyed, effect);
        }

        // ── Scenario 4 — DTT isolates Kell-system ────────────────────────────

        [Fact]
        public void Scenario4_DTT_DestroyesK()
        {
            var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, "K");
            Assert.Equal(AntigenEffect.Destroyed, effect);
        }

        [Fact]
        public void Scenario4_DTT_GatesKRuleout()
        {
            // DTT destroys K — gated rule-out expected for anti-K on DTT run
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario4Id);
            Assert.Contains(result.GatedRuleouts, g => g.Antibody == "anti-K");
        }

        [Fact]
        public void Scenario4_TwoRunsExist()
        {
            var runs = _db.GetAllSpecimenRuns(DemoDataSeeder.Scenario4Id);
            Assert.Equal(2, runs.Count);
        }

        // ── Scenario 5 — Warm autoantibody with allo anti-c ──────────────────

        [Fact]
        public void Scenario5_AntiC_IsSuspectedAfterRrAbsorption()
        {
            // After rr absorption c+ cells are still reactive → anti-c present
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario5Id);
            Assert.Contains("anti-c", result.Suspected.Keys);
        }

        [Fact]
        public void Scenario5_TwoRunsExist()
        {
            var runs = _db.GetAllSpecimenRuns(DemoDataSeeder.Scenario5Id);
            Assert.Equal(2, runs.Count);
        }

        [Fact]
        public void Scenario5_RrRun_SerumTreatmentIsRr()
        {
            var runs = _db.GetAllSpecimenRuns(DemoDataSeeder.Scenario5Id);
            Assert.Contains(runs, r => r.SerumTreatment == SerumTreatment.AlloAdsorptionRr);
        }

        // ── Scenario 6 — Multiple antibodies anti-K + anti-Jka ───────────────

        [Fact]
        public void Scenario6_BothAntibodiesSuspected()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario6Id);
            Assert.Contains("anti-K", result.Suspected.Keys);
            Assert.Contains("anti-Jka", result.Suspected.Keys);
        }

        [Fact]
        public void Scenario6_CombinationDetected()
        {
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario6Id);
            Assert.True(result.Combinations.Count > 0,
                "Expected at least one antibody combination to be detected");
        }

        // ── Scenario 7 — Prewarmed panel reveals anti-e ───────────────────────

        [Fact]
        public void Scenario7_AntiE_IsSuspectedFromPrewarmedRun()
        {
            // Prewarmed run has IS=NT everywhere; e+ cells still react at AHG
            var result = _analyzer.AnalyzeSpecimen(DemoDataSeeder.Scenario7Id);
            Assert.Contains("anti-e", result.Suspected.Keys);
        }

        [Fact]
        public void Scenario7_PrewarmedRun_ISPhaseNotInterpretable()
        {
            var runs = _db.GetAllSpecimenRuns(DemoDataSeeder.Scenario7Id);
            var pwRun = runs.First(r => r.SerumTreatment == SerumTreatment.Prewarmed);
            var ctx = new RunContext(pwRun);
            Assert.False(ctx.IsPhaseInterpretable("IS"),
                "IS phase should be non-interpretable for prewarmed serum");
        }

        [Fact]
        public void Scenario7_PrewarmedRun_AHGPhaseInterpretable()
        {
            var runs = _db.GetAllSpecimenRuns(DemoDataSeeder.Scenario7Id);
            var pwRun = runs.First(r => r.SerumTreatment == SerumTreatment.Prewarmed);
            var ctx = new RunContext(pwRun);
            Assert.True(ctx.IsPhaseInterpretable("AHG"),
                "AHG phase should be interpretable for prewarmed serum");
        }

        // ── Treatment model lookup tests ──────────────────────────────────────

        [Fact]
        public void TreatmentModel_FicinDestroysExpectedAntigens()
        {
            var destroyed = new[] { "M", "N", "S", "s", "Fya", "Fyb", "Xga", "Lea", "Leb" };
            foreach (var ag in destroyed)
            {
                var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, ag);
                Assert.True(effect == AntigenEffect.Destroyed, $"Expected {ag} to be Destroyed by ficin, got {effect}");
            }
        }

        [Fact]
        public void TreatmentModel_FicinEnhancesRhAntigens()
        {
            var enhanced = new[] { "D", "C", "c", "E", "e" };
            foreach (var ag in enhanced)
            {
                var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.Ficin, ag);
                Assert.True(effect == AntigenEffect.Enhanced, $"Expected {ag} to be Enhanced by ficin, got {effect}");
            }
        }

        [Fact]
        public void TreatmentModel_DTTDestroysKellSystemAntigens()
        {
            var destroyed = new[] { "K", "k", "Kpa", "Kpb", "Jsa", "Jsb", "Lua", "Lub" };
            foreach (var ag in destroyed)
            {
                var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, ag);
                Assert.True(effect == AntigenEffect.Destroyed, $"Expected {ag} to be Destroyed by DTT, got {effect}");
            }
        }

        [Fact]
        public void TreatmentModel_DTTDoesNotDestroyRhAntigens()
        {
            var rh = new[] { "D", "C", "c", "E", "e" };
            foreach (var ag in rh)
            {
                var effect = AntigenTreatmentEffects.GetCellEffect(CellTreatment.DTT, ag);
                Assert.True(effect != AntigenEffect.Destroyed, $"DTT should not destroy Rh antigen {ag}, got {effect}");
            }
        }

        [Fact]
        public void TreatmentModel_PrewarmedSuppressesISOnly()
        {
            var phases = AntigenTreatmentEffects.GetNonInterpretablePhases(SerumTreatment.Prewarmed);
            Assert.Contains("IS", phases);
            Assert.DoesNotContain("AHG", phases);
            Assert.DoesNotContain("C37", phases);
        }

        // ── CC fix regression tests ───────────────────────────────────────────

        [Fact]
        public void CCFix_PositiveCCShouldNotMakeReactionPositive()
        {
            var rxn = new Reaction { IS = "NT", C37 = "NT", AHG = "0", CC = "2+" };
            Assert.False(rxn.IsPositive, "CC 2+ with AHG 0 should NOT be positive");
            Assert.True(rxn.IsNegative, "CC 2+ with AHG 0 should be negative (valid IAT negative)");
        }

        [Fact]
        public void CCFix_AHGPositiveIsPositive()
        {
            var rxn = new Reaction { IS = "NT", C37 = "NT", AHG = "2+", CC = "NT" };
            Assert.True(rxn.IsPositive);
            Assert.False(rxn.IsNegative);
        }

        [Fact]
        public void CCFix_AllNegativeIsNegative()
        {
            var rxn = new Reaction { IS = "0", C37 = "0", AHG = "0", CC = "2+" };
            Assert.True(rxn.IsNegative);
            Assert.False(rxn.IsPositive);
        }
    }
}
