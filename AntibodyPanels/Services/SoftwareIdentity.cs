using System.Reflection;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Product identity and intended-use labeling for 510(k) software documentation.
    /// </summary>
    public static class SoftwareIdentity
    {
        public const string ProductName = "Antibody Panel Management System";

        public const string IntendedUse =
            "Decision-support software for licensed immunohematology professionals to document " +
            "antibody-panel reactions and compute rule-out and probability statistics. " +
            "A qualified professional must confirm identification. This software does not issue " +
            "or release blood units and is not a substitute for licensed clinical judgment.";

        public static string Version
        {
            get
            {
                var asm = typeof(SoftwareIdentity).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                    return info.Split('+')[0];
                return asm.GetName().Version?.ToString() ?? "2.0.0";
            }
        }

        public static string VersionLabel => $"Version {Version}";

        public static string ReportPreamble()
        {
            return $"{ProductName}  {VersionLabel}{Environment.NewLine}" +
                   $"INTENDED USE: {IntendedUse}{Environment.NewLine}{Environment.NewLine}";
        }

        public static string AboutText()
        {
            return $"{ProductName}\n\n" +
                   $"{VersionLabel} (C# / WPF)\n\n" +
                   $"{IntendedUse}\n\n" +
                   "Press F1 for keyboard shortcuts.";
        }
    }
}
