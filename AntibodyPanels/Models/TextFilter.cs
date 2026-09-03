using System;

namespace AntibodyPanels.Models
{
    public static class TextFilter
    {
        public static bool Matches(string? query, params string?[] fields)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var q = query.Trim();
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field) &&
                    field.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
