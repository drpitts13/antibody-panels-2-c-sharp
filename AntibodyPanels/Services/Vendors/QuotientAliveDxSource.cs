using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class QuotientAliveDxSource : VendorPanelSourceBase
    {
        public const string UsProductsUrl = "https://alivedx.com/us-products/";

        private static readonly Regex PdfHref = new(
            @"https://alivedx\.com/[^""'\s>]+\.pdf",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LotInName = new(
            @"V\d{6}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public QuotientAliveDxSource(VendorHttpClient http) : base(http) { }

        public override string VendorId => VendorIds.Quotient;
        public override string DisplayName => "Quotient / AliveDx";
        public override bool CanListLots => true;
        public override string? ListLotsUnavailableReason => null;

        public override async Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(
            CancellationToken cancellationToken = default)
        {
            var html = await Http.GetStringAsync(UsProductsUrl, cancellationToken).ConfigureAwait(false);
            return ParseProductHtml(html);
        }

        public IReadOnlyList<VendorLotListing> ParseProductHtml(string html)
        {
            var lots = new List<VendorLotListing>();
            foreach (Match href in PdfHref.Matches(html))
            {
                var url = href.Value;
                var file = System.IO.Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
                var lotMatch = LotInName.Match(file);
                if (!lotMatch.Success && !LotInName.IsMatch(url))
                    continue;
                if (!lotMatch.Success)
                    lotMatch = LotInName.Match(url);
                var enzyme = url.Contains("Z472", StringComparison.OrdinalIgnoreCase) ||
                             file.Contains("papain", StringComparison.OrdinalIgnoreCase);
                lots.Add(new VendorLotListing
                {
                    Vendor = VendorId,
                    ProductLine = enzyme ? "ALBAcyte Antibody ID (papain)" : "ALBAcyte Antibody ID",
                    CatalogNumber = url.Contains("Z473", StringComparison.OrdinalIgnoreCase) ? "Z473U"
                        : enzyme ? "Z472U" : "Z471U",
                    LotNumber = lotMatch.Success ? lotMatch.Value.ToUpperInvariant() : file,
                    DownloadUrl = url,
                    SourceFormat = "pdf",
                    EnzymeTreated = enzyme
                });
            }
            return lots
                .GroupBy(l => l.LotNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
    }
}
