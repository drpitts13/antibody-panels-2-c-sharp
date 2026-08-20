using System;
using System.Windows;
using System.Windows.Controls;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;
using Microsoft.Win32;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class PurgeDatabaseDialog : Window
    {
        private readonly DatabaseService _db;

        public int DeletedCount { get; private set; }
        public string? ArchivePath { get; private set; }
        public string SizeAfterDisplay { get; private set; } = "";

        public PurgeDatabaseDialog(DatabaseService db)
        {
            _db = db;
            InitializeComponent();
            BeforeDatePicker.SelectedDate = DateTime.Today.AddYears(-1);
            RefreshCapacity();
            UpdatePreview();
        }

        private DatabaseCapacityStatus CurrentCapacity()
        {
            var maxBytes = DatabaseCapacityStatus.BytesFromMb(AppSettings.Current.MaxDatabaseSizeMb);
            return _db.GetCapacityStatus(maxBytes);
        }

        private void RefreshCapacity()
        {
            var cap = CurrentCapacity();
            CapacityText.Text =
                $"Database size: {DatabaseCapacityStatus.FormatBytes(cap.FileBytes)} of " +
                $"{DatabaseCapacityStatus.FormatBytes(cap.MaxBytes)} ({cap.PercentUsed:0}% full).";
        }

        private bool TryGetCutoff(out string cutoff, out string? error)
        {
            cutoff = "";
            error = null;
            if (KeepDaysRadio.IsChecked == true)
            {
                if (!int.TryParse(KeepDaysBox.Text.Trim(), out var days) || days < 1 || days > 3650)
                {
                    error = "Days to keep must be between 1 and 3650.";
                    return false;
                }
                cutoff = DatabaseService.CutoffForKeepDays(days);
                return true;
            }

            if (BeforeDatePicker.SelectedDate is not DateTime date)
            {
                error = "Choose a cutoff date.";
                return false;
            }
            cutoff = date.ToString("yyyy-MM-dd");
            return true;
        }

        private void CutoffChanged(object sender, RoutedEventArgs e) => UpdatePreview();

        private void CutoffTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void CutoffDateChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

        private void KeepDaysBox_GotFocus(object sender, RoutedEventArgs e) =>
            KeepDaysRadio.IsChecked = true;

        private void BeforeDatePicker_GotFocus(object sender, RoutedEventArgs e) =>
            BeforeDateRadio.IsChecked = true;

        private void UpdatePreview()
        {
            if (PreviewText == null) return;
            if (!TryGetCutoff(out var cutoff, out var error))
            {
                PreviewText.Text = error ?? "";
                return;
            }

            var count = _db.CountSpecimensCreatedBefore(cutoff);
            PreviewText.Text = count == 0
                ? $"No specimens were created before {cutoff}."
                : $"This will permanently delete {count} specimen(s) created before {cutoff}, including their reactions and analysis.";
        }

        private void PurgeClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetCutoff(out var cutoff, out var error))
            {
                MessageBox.Show(error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var count = _db.CountSpecimensCreatedBefore(cutoff);
            if (count == 0)
            {
                MessageBox.Show("No specimens match this cutoff.",
                    "Purge Database", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? archivePath = null;
            if (ArchiveCheck.IsChecked == true)
            {
                var save = new SaveFileDialog
                {
                    Title = "Save purged-data archive",
                    Filter = "SQLite Database|*.db",
                    DefaultExt = "db",
                    FileName = $"antibody_panels_archive_{DateTime.Today:yyyyMMdd}.db"
                };
                if (save.ShowDialog(this) != true)
                    return;
                archivePath = save.FileName;
            }

            var confirm = MessageBox.Show(
                $"Permanently delete {count} specimen(s) created before {cutoff}?\n\n" +
                (archivePath != null
                    ? "An archive of the purged data will be created first. Archives contain specimen records and should be stored securely."
                    : "This cannot be undone. No archive will be created."),
                "Confirm purge",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                var result = _db.PurgeSpecimensCreatedBefore(cutoff, archivePath);
                DeletedCount = result.SpecimensDeleted;
                ArchivePath = result.ArchivePath;
                SizeAfterDisplay = DatabaseCapacityStatus.FormatBytes(result.FileSizeBytesAfter);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Purge failed:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
