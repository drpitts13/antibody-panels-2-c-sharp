using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Encapsulates the serology interpretation rules for one panel run.
    /// The analyzer uses this to gate rule-outs, weight evidence, and
    /// identify phases that should not be interpreted for antibody identification.
    /// </summary>
    public class RunContext
    {
        public PanelRun Run { get; }

        private readonly HashSet<string> _nonInterpretablePhases;
        private readonly IReadOnlyList<string> _absorbedAntibodies;
        private readonly HashSet<string> _extraAntigens;

        public RunContext(PanelRun run, IEnumerable<string>? extraAntigens = null)
        {
            Run = run;
            _nonInterpretablePhases = new HashSet<string>(
                AntigenTreatmentEffects.GetNonInterpretablePhases(run.SerumTreatment),
                StringComparer.OrdinalIgnoreCase);
            _absorbedAntibodies = AntigenTreatmentEffects.GetAbsorbedAntibodies(run.SerumTreatment);
            _extraAntigens = extraAntigens != null
                ? new HashSet<string>(extraAntigens, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Warehouse antigens typed on this run's panel.
        /// </summary>
        public IReadOnlyCollection<string> ExtraAntigens => _extraAntigens;

        /// <summary>
        /// Standard antigens are always typed. Warehouse antigens are typed only
        /// when they have been added to this panel.
        /// </summary>
        public bool TypesAntigen(string antigen) =>
            AntigenConstants.IsStandard(antigen) || _extraAntigens.Contains(antigen);

        // ── Antigen queries ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the effective antigen expression string for a given cell.
        /// If the cell treatment destroys the antigen, returns "-" regardless of
        /// the panel cell's typed value.
        /// </summary>
        public string EffectiveAntigen(PanelCell cell, string antigen)
        {
            var effect = AntigenTreatmentEffects.GetCellEffect(Run.CellTreatment, antigen);
            return effect == AntigenEffect.Destroyed ? "-" : cell.GetAntigen(antigen);
        }

        /// <summary>
        /// Returns true when the antigen is effectively present on the cell
        /// (taking cell treatment into account).
        /// </summary>
        public bool IsAntigenPresent(PanelCell cell, string antigen) =>
            EffectiveAntigen(cell, antigen) == "+";

        /// <summary>
        /// Returns the AntigenEffect of the cell treatment on the given antigen.
        /// </summary>
        public AntigenEffect GetAntigenEffect(string antigen) =>
            AntigenTreatmentEffects.GetCellEffect(Run.CellTreatment, antigen);

        // ── Phase queries ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the given reaction phase is interpretable for
        /// antibody identification in this run (e.g. IS is suppressed by prewarm).
        /// </summary>
        public bool IsPhaseInterpretable(string phase) =>
            !_nonInterpretablePhases.Contains(phase);

        /// <summary>
        /// Returns the reaction value for a phase, substituting "NT" when the
        /// phase is not interpretable for this run.
        /// </summary>
        public string GetInterpretedPhaseValue(Reaction rxn, string phase)
        {
            if (!IsPhaseInterpretable(phase)) return "NT";
            return phase switch
            {
                "IS"  => rxn.IS,
                "C37" => rxn.C37,
                "AHG" => rxn.AHG,
                _     => "NT",
            };
        }

        // ── Rule-out gating ───────────────────────────────────────────────────

        /// <summary>
        /// Returns true when a negative reaction on <paramref name="cell"/>
        /// can legitimately contribute a rule-out for <paramref name="antigen"/>.
        /// <para>
        /// A rule-out is blocked when the antigen is destroyed by the cell treatment
        /// (the cell would always be negative for that antigen regardless of antibody presence).
        /// </para>
        /// </summary>
        public bool CanContributeRuleout(string antigen, PanelCell cell) =>
            GetAntigenEffect(antigen) != AntigenEffect.Destroyed &&
            cell.GetAntigen(antigen) == "+";

        // ── Serum absorption queries ───────────────────────────────────────────

        /// <summary>
        /// Antibody specificities that the serum treatment is expected to have
        /// removed from the test serum (empty for untreated/prewarm).
        /// </summary>
        public IReadOnlyList<string> AbsorbedAntibodies => _absorbedAntibodies;

        public bool IsAutoAdsorbed =>
            Run.SerumTreatment == SerumTreatment.AutoAdsorption;

        // ── Evidence weighting ────────────────────────────────────────────────

        /// <summary>
        /// A multiplier (0–1) applied to the statistical weight of reactions from
        /// this run.  Untreated runs carry full weight.  Enzyme-treated runs are
        /// complementary evidence and get slightly discounted to avoid double-counting
        /// when combined with an untreated run.
        /// </summary>
        public double EvidenceWeight => Run.CellTreatment switch
        {
            CellTreatment.None   => 1.0,
            CellTreatment.Ficin  => 0.7,
            CellTreatment.Papain => 0.7,
            CellTreatment.DTT    => 0.8,
            _                    => 1.0,
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether a reaction is negative under this run's interpretable phases.
        /// </summary>
        public bool IsNegative(Reaction rxn)
        {
            var ahg = GetInterpretedPhaseValue(rxn, "AHG");
            var isVal = GetInterpretedPhaseValue(rxn, "IS");
            var c37 = GetInterpretedPhaseValue(rxn, "C37");
            return ahg == "0" && IsNtOrZero(isVal) && IsNtOrZero(c37);
        }

        /// <summary>
        /// Checks whether a reaction is positive under this run's interpretable phases.
        /// </summary>
        public bool IsPositive(Reaction rxn)
        {
            return IsReactionStrong(GetInterpretedPhaseValue(rxn, "IS"))
                || IsReactionStrong(GetInterpretedPhaseValue(rxn, "C37"))
                || IsReactionStrong(GetInterpretedPhaseValue(rxn, "AHG"));
        }

        /// <summary>
        /// Returns the strongest (phase, value) pair restricted to interpretable phases.
        /// </summary>
        public (string phase, string value) GetStrongestPhase(Reaction rxn)
        {
            var candidates = new[] { "IS", "C37", "AHG" }
                .Where(IsPhaseInterpretable)
                .Select(ph => (ph, val: GetInterpretedPhaseValue(rxn, ph)));

            string bestPhase = "", bestVal = "0";
            foreach (var (ph, val) in candidates)
            {
                if (val == "NT" || val == "0" || string.IsNullOrEmpty(val)) continue;
                if (ReactionToNumeric(val) > ReactionToNumeric(bestVal))
                {
                    bestVal = val;
                    bestPhase = ph;
                }
            }
            return (bestPhase, bestVal);
        }

        private static bool IsNtOrZero(string v) =>
            v == "NT" || v == "0" || string.IsNullOrEmpty(v);

        private static bool IsReactionStrong(string v) =>
            !IsNtOrZero(v);

        internal static double ReactionToNumeric(string reaction)
        {
            if (string.IsNullOrEmpty(reaction) || reaction == "NT" || reaction == "0") return 0;
            if (int.TryParse(reaction.Replace("+", ""), out int n)) return n;
            return 0;
        }
    }
}
