using System;
using System.IO;
using System.Text;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public enum ArtifactIntegrity
    {
        None,
        Missing,
        Present,
        HashMismatch
    }

    public sealed class ArtifactInspection
    {
        public ArtifactIntegrity Status { get; init; }
        public string? Path { get; init; }
        public string? ExpectedSha256 { get; init; }
        public string? ActualSha256 { get; init; }
        public long? ByteLength { get; init; }
        public string Explanation { get; init; } = string.Empty;
        public string? TextPreview { get; init; }
        public bool CanOpen => Status is ArtifactIntegrity.Present or ArtifactIntegrity.HashMismatch
                               && !string.IsNullOrWhiteSpace(Path) && File.Exists(Path);
    }

    public static class ImportArtifactInspector
    {
        public static ArtifactInspection Inspect(Panel panel) =>
            Inspect(panel.SourceArtifactPath, panel.SourceSha256);

        public static ArtifactInspection Inspect(string? path, string? expectedSha, int previewLines = 24)
        {
            if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(expectedSha))
            {
                return new ArtifactInspection
                {
                    Status = ArtifactIntegrity.None,
                    Explanation = "No source artifact was retained for this panel."
                };
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new ArtifactInspection
                {
                    Status = ArtifactIntegrity.Missing,
                    Path = path,
                    ExpectedSha256 = expectedSha,
                    Explanation = string.IsNullOrWhiteSpace(expectedSha)
                        ? "The recorded artifact path is missing and no SHA-256 is stored."
                        : $"The source file is missing. Expected SHA-256 {expectedSha}."
                };
            }

            var bytes = File.ReadAllBytes(path);
            var actual = PanelImportReviewer.Hash(bytes);
            var match = string.IsNullOrWhiteSpace(expectedSha)
                || string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase);
            var status = match ? ArtifactIntegrity.Present : ArtifactIntegrity.HashMismatch;
            var explanation = match
                ? $"Source artifact is present ({bytes.Length} bytes). SHA-256 {actual} matches the stored hash."
                : $"Source artifact is present but the file does not match the stored hash. Expected {expectedSha}; file is {actual}. Review before using this panel.";
            if (string.IsNullOrWhiteSpace(expectedSha))
                explanation = $"Source artifact is present ({bytes.Length} bytes). SHA-256 {actual}. No stored hash was recorded to compare.";

            return new ArtifactInspection
            {
                Status = status,
                Path = path,
                ExpectedSha256 = expectedSha,
                ActualSha256 = actual,
                ByteLength = bytes.Length,
                Explanation = explanation,
                TextPreview = TryPreview(path, bytes, previewLines)
            };
        }

        private static string? TryPreview(string path, byte[] bytes, int previewLines)
        {
            var ext = System.IO.Path.GetExtension(path);
            if (!ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                return null;
            if (bytes.Length > 0 && bytes[0] < 9)
                return null;
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sb = new StringBuilder();
            for (var i = 0; i < previewLines; i++)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
