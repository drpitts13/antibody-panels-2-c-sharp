using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class OrthoEAntigramSource : VendorPanelSourceBase
    {
        public const string QuickAntigramUrl =
            "https://www.quidelortho.com/jp/ja/resources/quick-antigram-pdf";

        private static readonly Regex LotCell = new(
            @"\b((?:RA|RB|RC)\d{3})\b",
            RegexOptions.Compiled);
        private static readonly Regex PdfHref = new(
            @"(?:https://www\.quidelortho\.com)?(/content/dam/quidelortho/[^""'\s>]+\.pdf)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public OrthoEAntigramSource(VendorHttpClient http) : base(http) { }

        public override string VendorId => VendorIds.Ortho;
        public override string DisplayName => "Ortho / QuidelOrtho";
        public override bool CanListLots => true;
        public override string? ListLotsUnavailableReason => null;

        public override async Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(
            CancellationToken cancellationToken = default)
        {
            var html = await Http.GetStringAsync(QuickAntigramUrl, cancellationToken).ConfigureAwait(false);
            return ParseQuickAntigramHtml(html);
        }

        public IReadOnlyList<VendorLotListing> ParseQuickAntigramHtml(string html)
        {
            var pdfs = PdfHref.Matches(html)
                .Select(m => "https://www.quidelortho.com" + m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var lots = new List<VendorLotListing>();
            foreach (Match m in LotCell.Matches(html))
            {
                var lot = m.Value;
                var product = lot.StartsWith("RA", StringComparison.OrdinalIgnoreCase) ? "Resolve Panel A"
                    : lot.StartsWith("RB", StringComparison.OrdinalIgnoreCase) ? "Resolve Panel B"
                    : "Resolve Panel C";
                var nearby = html.Substring(Math.Max(0, m.Index - 200),
                    Math.Min(400, html.Length - Math.Max(0, m.Index - 200)));
                var pdf = pdfs.FirstOrDefault(u =>
                    u.Contains(lot, StringComparison.OrdinalIgnoreCase) ||
                    nearby.Contains(u, StringComparison.OrdinalIgnoreCase));
                if (pdf == null && pdfs.Count > 0)
                    pdf = pdfs[0];
                lots.Add(new VendorLotListing
                {
                    Vendor = VendorId,
                    ProductLine = product,
                    LotNumber = lot,
                    DownloadUrl = pdf,
                    SourceFormat = "pdf",
                    Notes = pdf == null
                        ? "Japan Quick Antigram listing; US E-Antigrams may require QuidelOrtho Technical Documents."
                        : "Listed on QuidelOrtho Quick Antigram page. Barcode-only sheets will not parse as antigrams."
                });
            }
            return lots
                .GroupBy(l => l.LotNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
    }
}
