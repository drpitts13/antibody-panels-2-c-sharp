using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AntibodyPanels.Services
{
    public sealed class SchemaDiff
    {
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();
        public bool Changed => Added.Count > 0 || Removed.Count > 0;
    }

    public sealed class PanelImportReview
    {
        public int PanelId { get; set; }
        public string? Sha256 { get; set; }
        public string? ArtifactPath { get; set; }
        public string? PriorLotNumber { get; set; }
        public int? PriorPanelId { get; set; }
        public SchemaDiff Schema { get; set; } = new();
        public bool StoredInactive { get; set; } = true;
        public string Explanation { get; set; } = string.Empty;
    }

    public static class PanelImportReviewer
    {
        public static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        public static SchemaDiff CompareSchema(IEnumerable<string> incoming, IEnumerable<string> prior)
        {
            var next = Normalize(incoming);
            var prev = Normalize(prior);
            var diff = new SchemaDiff();
            diff.Added.AddRange(next.Where(a => !prev.Contains(a)));
            diff.Removed.AddRange(prev.Where(a => !next.Contains(a)));
            return diff;
        }

        public static string Explain(PanelImportReview review, string? lotNumber, string? vendor)
        {
            var sb = new StringBuilder();
            var lot = string.IsNullOrWhiteSpace(lotNumber) ? "this lot" : lotNumber.Trim();
            var vendorLabel = string.IsNullOrWhiteSpace(vendor) ? "the vendor" : vendor.Trim();
            sb.Append(lot).Append(" from ").Append(vendorLabel)
                .Append(" was stored inactive pending review.");
            if (!string.IsNullOrWhiteSpace(review.Sha256))
                sb.Append(" Source artifact SHA-256 ").Append(review.Sha256)
                    .Append(" was retained");
            else
                sb.Append(" No source artifact was retained (bytes were not supplied)");
            sb.Append('.');
            if (string.IsNullOrWhiteSpace(review.PriorLotNumber))
            {
                sb.Append(" No prior lot from this vendor/product line was available to compare.");
            }
            else if (!review.Schema.Changed)
            {
                sb.Append(" Antigen schema matches prior lot ").Append(review.PriorLotNumber).Append('.');
            }
            else
            {
                sb.Append(" Antigen schema differs from prior lot ").Append(review.PriorLotNumber).Append('.');
                if (review.Schema.Added.Count > 0)
                    sb.Append(" Added: ").Append(string.Join(", ", review.Schema.Added)).Append('.');
                if (review.Schema.Removed.Count > 0)
                    sb.Append(" Removed: ").Append(string.Join(", ", review.Schema.Removed)).Append('.');
            }
            sb.Append(" Activate only after a qualified user reviews the antigram. This is not a diagnosis.");
            return sb.ToString();
        }

        private static List<string> Normalize(IEnumerable<string> antigens) =>
            antigens
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
