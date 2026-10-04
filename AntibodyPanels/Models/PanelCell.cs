using System.Collections.Generic;

namespace AntibodyPanels.Models
{
    public class PanelCell
    {
        public int Id { get; set; }
        public int PanelId { get; set; }
        public string CellNumber { get; set; } = string.Empty;
        public string? DonorId { get; set; }
        public string? RhPhenotype { get; set; }
        public string? SpecialTypes { get; set; }

        // Antigen values keyed by antigen name (e.g. "D", "C", "c", ...)
        public Dictionary<string, string> Antigens { get; set; } = new();

        public string GetAntigen(string antigen) =>
            TryGetTyped(antigen, out var v)
                ? (AntigenConstants.IsExplicitHomozygousValue(v) ? "+" : v)
                : "-";

        public string? GetTypedValue(string antigen) =>
            TryGetTyped(antigen, out var v) ? v : null;

        /// <summary>
        /// True when this cell has a typed +/−/++ value for the antigen.
        /// Missing, blank, or NT is not the same as antigen-negative.
        /// </summary>
        public bool HasTypedAntigen(string antigen) => TryGetTyped(antigen, out _);

        public bool IsAntigenPositive(string antigen) =>
            TryGetTyped(antigen, out var v) && AntigenConstants.IsAntigenPositiveValue(v);

        public bool IsExplicitlyHomozygous(string antigen) =>
            TryGetTyped(antigen, out var v) && AntigenConstants.IsExplicitHomozygousValue(v);

        /// <summary>
        /// Homozygous when the source marked ++, or when the antithetical partner
        /// is typed negative. An untyped partner is not treated as negative.
        /// A typed partner + overrides a ++ mark (conflict — not homozygous).
        /// </summary>
        public bool IsHomozygousFor(string antigen)
        {
            if (!IsAntigenPositive(antigen)) return false;
            AntigenConstants.AntitheticalPairs.TryGetValue(antigen, out var partner);
            if (partner != null && HasTypedAntigen(partner) && GetAntigen(partner) == "+")
                return false;
            if (IsExplicitlyHomozygous(antigen)) return true;
            return partner != null && HasTypedAntigen(partner) && GetAntigen(partner) == "-";
        }

        public void SetAntigen(string antigen, string value) =>
            Antigens[antigen] = value;

        private bool TryGetTyped(string antigen, out string value)
        {
            if (Antigens.TryGetValue(antigen, out var v) && AntigenConstants.IsTypedAntigenValue(v))
            {
                value = v;
                return true;
            }
            value = "";
            return false;
        }
    }
}
