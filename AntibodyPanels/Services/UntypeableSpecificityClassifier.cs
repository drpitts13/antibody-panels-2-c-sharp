using System;
using System.Collections.Generic;
using System.Linq;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Names antibodies the linked panels cannot type because the antigen was
    /// never imported. Decision support only — not a rule-out or identification.
    /// Warehouse antigens stay silent unless a panel actually types them.
    /// </summary>
    public static class UntypeableSpecificityClassifier
    {
        public static List<UntypeableSpecificity> Classify(IReadOnlyCollection<string>? typedOnPanels)
        {
            var typed = typedOnPanels as HashSet<string>
                ?? new HashSet<string>(typedOnPanels ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (typed.Count == 0)
                return new List<UntypeableSpecificity>();

            var cs = new HashSet<string>(AntigenConstants.ClinicallySignificantAntigens, StringComparer.Ordinal);
            var list = new List<UntypeableSpecificity>();
            foreach (var ag in AntigenConstants.Antigens)
            {
                if (typed.Contains(ag)) continue;
                var clinicallySignificant = cs.Contains(ag);
                list.Add(new UntypeableSpecificity
                {
                    Antibody = $"anti-{ag}",
                    Antigen = ag,
                    IsClinicallySignificant = clinicallySignificant,
                    Explanation = FormatExplanation(ag, clinicallySignificant),
                });
            }

            return list;
        }

        public static string FormatExplanation(string antigen, bool clinicallySignificant)
        {
            var cs = clinicallySignificant ? " (clinically significant)" : "";
            return $"anti-{antigen}{cs} cannot be typed from the current panels because {antigen} " +
                   "was never imported or tested on any linked cell. This is not a rule-out and not an identification.";
        }
    }
}
