using System;
using System.IO;
using System.Text.Json;
using AntibodyPanels.Models;

namespace AntibodyPanels.Services
{
    public static class AppSettings
    {
        public static LabSettings Current { get; set; } = LabSettings.CreateDefault();

        public static event EventHandler? Changed;

        public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
    }

    public static class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string DefaultPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AntibodyPanels",
                "settings.json");

        public static void Load(string? path = null)
        {
            path ??= DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<LabSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.Clamp();
                        AppSettings.Current = loaded;
                        AppSettings.RaiseChanged();
                        return;
                    }
                }
            }
            catch
            {
                // Keep defaults if the file is missing or corrupt.
            }

            AppSettings.Current = LabSettings.CreateDefault();
            AppSettings.RaiseChanged();
        }

        public static void Save(string? path = null)
        {
            path ??= DefaultPath;
            AppSettings.Current.Clamp();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(AppSettings.Current, JsonOptions));
            AppSettings.RaiseChanged();
        }
    }
}
