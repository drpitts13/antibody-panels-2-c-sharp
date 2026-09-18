using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    public sealed class BioRadIhAreaSource : VendorPanelSourceBase
    {
        public const string ProductPageUrl =
            "https://ih-area.bio-rad.com/product/OUS/Reagents/Gel/Test_Cells_for_Antibody_Identification/ID-DiaPanel";
        public const string DiamedTablesUrl = "https://diamed.com.br/tabelas-de-antigenos/";

        private static readonly Regex LotPattern = new(
            @"\b((?:45161|45171)(?:\.\d+){1,3})\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExpPattern = new(
            @"(?:Exp(?:\.|iration)?\s*date|Vencimento)\s*:\s*(\d{4}[.\-]\d{1,2}[.\-]\d{1,2})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PdfHref = new(
            @"https?://[^""'\s>]+?\.pdf",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BackendPdf = new(
            @"https://backend\.ih-area\.bio-rad\.com/system/files/[^""'\s>]+\.pdf",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public BioRadIhAreaSource(VendorHttpClient http) : base(http) { }

        public override string VendorId => VendorIds.BioRad;
        public override string DisplayName => "Bio-Rad";
        public override bool CanListLots => true;
        public override string? ListLotsUnavailableReason => null;

        public override async Task<IReadOnlyList<VendorLotListing>> ListLotsAsync(
            CancellationToken cancellationToken = default)
        {
            var lots = new List<VendorLotListing>();
            try
            {
                var html = await Http.GetStringAsync(ProductPageUrl, cancellationToken).ConfigureAwait(false);
                lots.AddRange(ParseIhAreaHtml(html));
            }
            catch
            {
                // Fall through to Diamed.
            }

            if (lots.Count == 0)
            {
                try
                {
                    var html = await Http.GetStringAsync(DiamedTablesUrl, cancellationToken).ConfigureAwait(false);
                    lots.AddRange(ParseDiamedHtml(html));
                }
                catch
                {
                    // No catalog this attempt.
                }
            }

            return Dedup(lots);
        }

        public IReadOnlyList<VendorLotListing> ParseIhAreaHtml(string html)
        {
            var pdfs = BackendPdf.Matches(html).Select(m => m.Value)
                .Concat(PdfHref.Matches(html).Select(m => m.Value.Replace("http://", "https://")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(u => u.Contains("ih-area.bio-rad.com", StringComparison.OrdinalIgnoreCase)
                            || u.Contains("bio-rad.com", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var lots = new List<VendorLotListing>();
            foreach (Match lotMatch in LotPattern.Matches(html))
            {
                var lot = lotMatch.Groups[1].Value.Trim();
                var nearby = SliceAround(html, lotMatch.Index, 400);
                var exp = ExpPattern.Match(nearby);
                var enzyme = lot.StartsWith("45171", StringComparison.Ordinal);
                var pdf = pdfs.FirstOrDefault(u =>
                               u.Contains(lot, StringComparison.OrdinalIgnoreCase))
                           ?? pdfs.FirstOrDefault(u => LotLooksLikePdf(lot, u))
                           ?? pdfs.FirstOrDefault(u => nearby.Contains(u, StringComparison.OrdinalIgnoreCase));

                lots.Add(new VendorLotListing
                {
                    Vendor = VendorId,
                    ProductLine = enzyme ? "ID-DiaPanel-P" : "ID-DiaPanel",
                    CatalogNumber = enzyme ? "45171" : "45161",
                    LotNumber = lot,
                    ExpirationDate = exp.Success
                        ? VendorAntigramParser.NormalizeDate(exp.Groups[1].Value)
                        : null,
                    DownloadUrl = pdf,
                    SourceFormat = "pdf",
                    EnzymeTreated = enzyme
                });
            }

            if (lots.Count == 0)
            {
                foreach (var pdf in pdfs)
                {
                    lots.Add(new VendorLotListing
                    {
                        Vendor = VendorId,
                        ProductLine = "ID-DiaPanel",
                        CatalogNumber = "45161",
                        LotNumber = System.IO.Path.GetFileNameWithoutExtension(new Uri(pdf).AbsolutePath),
                        DownloadUrl = pdf,
                        SourceFormat = "pdf"
                    });
                }
            }

            return lots;
        }

        public IReadOnlyList<VendorLotListing> ParseDiamedHtml(string html)
        {
            var lots = new List<VendorLotListing>();
            var rowRegex = new Regex(
                @"<(?:tr|TR)[^>]*>[\s\S]*?ID-DiaPanel(-P)?[\s\S]*?(\d{7,})[\s\S]*?(\d{2}\.\d{2}\.\d{4})[\s\S]*?href=[""']([^""']+\.pdf)[""']",
                RegexOptions.IgnoreCase);
            foreach (Match m in rowRegex.Matches(html))
            {
                var enzyme = m.Groups[1].Success;
                var href = m.Groups[4].Value;
                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    href = new Uri(new Uri(DiamedTablesUrl), href).ToString();
                if (!href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;
                lots.Add(new VendorLotListing
                {
                    Vendor = VendorId,
                    ProductLine = enzyme ? "ID-DiaPanel-P" : "ID-DiaPanel",
                    CatalogNumber = enzyme ? "45171" : "45161",
                    LotNumber = m.Groups[2].Value,
                    ExpirationDate = DateTime.TryParseExact(m.Groups[3].Value, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        ? d.ToString("yyyy-MM-dd") : null,
                    DownloadUrl = href,
                    SourceFormat = "pdf",
                    EnzymeTreated = enzyme
                });
            }
            return lots;
        }

        private static bool LotLooksLikePdf(string lot, string pdfUrl)
        {
            var parts = lot.Split('.');
            if (parts.Length >= 2 &&
                pdfUrl.Contains($".{parts[1]}X", StringComparison.OrdinalIgnoreCase))
                return true;
            if (parts.Length >= 2 &&
                pdfUrl.Contains($"{parts[0]}.{parts[1]}", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static string SliceAround(string html, int index, int window)
        {
            var start = Math.Max(0, index - window);
            var len = Math.Min(html.Length - start, window * 2);
            return html.Substring(start, len);
        }

        private static IReadOnlyList<VendorLotListing> Dedup(IEnumerable<VendorLotListing> lots) =>
            lots
                .GroupBy(l => l.LotNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(l => l.ExpirationDate)
                .ToList();
    }
}
