using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AntibodyPanels.Services
{
    public sealed class ImportArtifactStore
    {
        public string DirectoryPath { get; }

        public ImportArtifactStore(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string Save(byte[] bytes, string? vendor, string? lotNumber, string? format, string sha256)
        {
            Directory.CreateDirectory(DirectoryPath);
            var ext = string.IsNullOrWhiteSpace(format) ? "bin" : format.Trim().TrimStart('.');
            if (ext.Equals("antigram", StringComparison.OrdinalIgnoreCase)) ext = "pdf";
            var name = $"{Sanitize(vendor)}_{Sanitize(lotNumber)}_{sha256[..Math.Min(12, sha256.Length)]}.{ext}";
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string Sanitize(string? value)
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
            var safe = Regex.Replace(trimmed, @"[^A-Za-z0-9._-]+", "_");
            return safe.Length > 40 ? safe[..40] : safe;
        }
    }
}
