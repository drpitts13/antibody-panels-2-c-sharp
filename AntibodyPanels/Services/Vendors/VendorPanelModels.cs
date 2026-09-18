using System;
using System.Collections.Generic;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services.Vendors
{
    public static class VendorIds
    {
        public const string BioRad = "Bio-Rad";
        public const string Immucor = "Immucor";
        public const string Ortho = "Ortho Clinical Diagnostics";
        public const string Grifols = "Grifols";
        public const string Medion = "Medion";
        public const string Quotient = "Quotient";

        public static readonly IReadOnlyList<string> All = new[]
        {
            BioRad, Immucor, Ortho, Grifols, Medion, Quotient
        };
    }

    public sealed class VendorLotListing
    {
        public string Vendor { get; init; } = string.Empty;
        public string ProductLine { get; init; } = string.Empty;
        public string? CatalogNumber { get; init; }
        public string LotNumber { get; init; } = string.Empty;
        public string? ExpirationDate { get; init; }
        public string? DownloadUrl { get; init; }
        public string SourceFormat { get; init; } = "pdf";
        public bool EnzymeTreated { get; init; }
        public string? Notes { get; init; }

        public string DisplayExpiration => ExpirationDate ?? "—";
    }

    public sealed class VendorParseResult
    {
        public string Vendor { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? LotNumber { get; set; }
        public string? ExpirationDate { get; set; }
        public string? CatalogNumber { get; set; }
        public string? ProductLine { get; set; }
        public bool EnzymeTreated { get; set; }
        public string? SourceUrl { get; set; }
        public string SourceFormat { get; set; } = "pdf";
        public string? SpecialNotes { get; set; }
        public List<PanelCell> Cells { get; } = new();
        public List<string> AntigenOrder { get; } = new();
        public List<string> Errors { get; } = new();
        public bool Success => Errors.Count == 0 && Cells.Count > 0;
    }
}
