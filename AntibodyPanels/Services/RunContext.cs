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
        private readonly HashSet<string>? _typedAntigens;

        public RunContext(PanelRun run, IEnumerable<string>? extraAntigens = null,
            IEnumerable<string>? typedAntigens = null)
        {
            Run = run;
            _nonInterpretablePhases = new HashSet<string>(
                AntigenTreatmentEffects.GetNonInterpretablePhases(run.SerumTreatment),
                StringComparer.OrdinalIgnoreCase);
            _absorbedAntibodies = AntigenTreatmentEffects.GetAbsorbedAntibodies(run.SerumTreatment);
            _extraAntigens = extraAntigens != null
                ? new HashSet<string>(extraAntigens, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            _typedAntigens = typedAntigens != null
                ? new HashSet<string>(typedAntigens, StringComparer.Ordinal)
                : null;
        }

        /// <summary>
        /// Warehouse antigens typed on this run's panel.
        /// </summary>
        public IReadOnlyCollection<string> ExtraAntigens => _extraAntigens;

        /// <summary>
        /// Antigens this run's panel types. When no typed set is supplied,
        /// standard antigens plus warehouse extras are assumed (manual panels).
        /// </summary>
        public IReadOnlyCollection<string> TypedAntigens =>
            _typedAntigens ?? (IReadOnlyCollection<string>)Array.Empty<string>();

        public bool TypesAntigen(string antigen) =>
            _typedAntigens != null
                ? _typedAntigens.Contains(antigen)
                : AntigenConstants.IsStandard(antigen) || _extraAntigens.Contains(antigen);

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
            cell.HasTypedAntigen(antigen) && EffectiveAntigen(cell, antigen) == "+";

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
        public bool IsPhaseInterpretable(string phase)
        {
            if (string.Equals(phase, "RT", StringComparison.OrdinalIgnoreCase)
                && _nonInterpretablePhases.Contains("IS"))
                return false;
            return !_nonInterpretablePhases.Contains(phase);
        }

        /// <summary>
        /// Returns the reaction value for a phase, substituting "NT" when the
        /// phase is not interpretable for this run.
        /// </summary>
        public string GetInterpretedPhaseValue(Reaction rxn, string phase)
        {
            if (!IsPhaseInterpretable(phase)) return "NT";
            if (phase is "IS") return rxn.IS;
            if (phase is "C37") return rxn.C37;
            if (phase is "AHG") return rxn.AHG;
            return rxn.ExtraPhases.TryGetValue(phase, out var extra) ? extra : "NT";
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
            if (!ReactionGrade.IsNegative(GetInterpretedPhaseValue(rxn, "AHG")))
                return false;
            return InterpretablePhaseNames(rxn)
                .Where(ph => ph != "AHG")
                .All(ph => ReactionGrade.IsAbsent(GetInterpretedPhaseValue(rxn, ph)));
        }

        /// <summary>
        /// Checks whether a reaction is positive under this run's interpretable phases.
        /// </summary>
        public bool IsPositive(Reaction rxn) =>
            InterpretablePhaseNames(rxn).Any(ph =>
                IsReactionStrong(GetInterpretedPhaseValue(rxn, ph)));

        /// <summary>
        /// Returns the strongest (phase, value) pair restricted to interpretable phases.
        /// </summary>
        public (string phase, string value) GetStrongestPhase(Reaction rxn)
        {
            string bestPhase = "", bestVal = ReactionGrade.Negative;
            foreach (var ph in InterpretablePhaseNames(rxn))
            {
                var val = GetInterpretedPhaseValue(rxn, ph);
                if (ReactionGrade.IsAbsent(val)) continue;
                if (ReactionToNumeric(val) > ReactionToNumeric(bestVal))
                {
                    bestVal = ReactionGrade.Normalize(val);
                    bestPhase = ph;
                }
            }
            return (bestPhase, bestVal);
        }

        private IEnumerable<string> InterpretablePhaseNames(Reaction rxn)
        {
            foreach (var phase in ExtraPhaseParser.CorePhases)
            {
                if (IsPhaseInterpretable(phase))
                    yield return phase;
            }
            foreach (var phase in rxn.ExtraPhases.Keys)
            {
                if (ExtraPhaseParser.IsDilution(phase)) continue;
                if (IsPhaseInterpretable(phase))
                    yield return phase;
            }
        }

        private static bool IsReactionStrong(string v) => ReactionGrade.IsPositive(v);

        internal static double ReactionToNumeric(string reaction) =>
            ReactionGrade.Strength(reaction);
    }
}
