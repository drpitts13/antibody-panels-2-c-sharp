using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AntibodyPanels.Services.Vendors
{
    /// <summary>
    /// Outbound HTTPS for vendor document catalogs only. Never send specimen or PHI fields.
    /// </summary>
    public sealed class VendorHttpClient : IDisposable
    {
        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "ih-area.bio-rad.com",
            "backend.ih-area.bio-rad.com",
            "www.bio-rad.com",
            "bio-rad.com",
            "diamed.com.br",
            "www.diamed.com.br",
            "www.quidelortho.com",
            "quidelortho.com",
            "techdocs.quidelortho.com",
            "alivedx.com",
            "www.alivedx.com",
            "www.diagnostic.grifols.com",
            "diagnostic.grifols.com",
            "transfusionandtransplant.werfen.com",
        };

        private readonly HttpClient _http;

        public VendorHttpClient()
        {
            _http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AntibodyPanels/2.0 (lab desktop; vendor document download)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/pdf,*/*");
        }

        public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        {
            using var resp = await SendAsync(url, cancellationToken).ConfigureAwait(false);
            return await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default)
        {
            using var resp = await SendAsync(url, cancellationToken).ConfigureAwait(false);
            return await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!TryValidateVendorUrl(url, out var error))
                throw new InvalidOperationException(error);
            var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return resp;
        }

        public static bool TryValidateVendorUrl(string? url, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                error = "Vendor download URL is missing or invalid.";
                return false;
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Vendor downloads must use HTTPS.";
                return false;
            }
            if (!AllowedHosts.Contains(uri.Host))
            {
                error = $"Host '{uri.Host}' is not an allowed vendor document site.";
                return false;
            }
            return true;
        }

        public void Dispose() => _http.Dispose();
    }
}
