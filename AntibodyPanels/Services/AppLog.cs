using System;
using System.IO;

namespace AntibodyPanels.Services
{
    /// <summary>
    /// Local application log. Do not write reaction grades, comments, or other PHI.
    /// </summary>
    public static class AppLog
    {
        public static string LogDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AntibodyPanels",
                "logs");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string message, Exception? ex = null)
        {
            if (ex == null)
            {
                Write("ERROR", message);
                return;
            }

            Write("ERROR", $"{message} {ex.GetType().Name}: {ex.Message}");
        }

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"{DateTime.Now:o} [{level}] v{SoftwareIdentity.Version} {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
            catch
            {
                // Logging must never break clinical workflow.
            }
        }
    }
}
