using System;
using System.Windows;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using Microsoft.Win32;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class RestoreArchiveDialog : Window
    {
        private readonly DatabaseService _db;
        private ArchiveInspection? _inspection;

        public RestoreResult? RestoreResult { get; private set; }

        public RestoreArchiveDialog(DatabaseService db, string? initialPath = null)
        {
            _db = db;
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(initialPath))
                LoadArchive(initialPath);
        }

        private void BrowseClick(object sender, RoutedEventArgs e)
        {
            var open = new OpenFileDialog
            {
                Title = "Open specimen archive",
                Filter = "SQLite Database|*.db|All Files|*.*",
                DefaultExt = "db"
            };
            if (open.ShowDialog(this) != true) return;
            LoadArchive(open.FileName);
        }

        private void LoadArchive(string path)
        {
            try
            {
                _inspection = _db.InspectArchive(path);
                PathBox.Text = _inspection.Path;
                SpecimenGrid.ItemsSource = _inspection.Specimens;
                RestoreButton.IsEnabled = _inspection.RestorableCount > 0;

                var range = _inspection.SpecimenCount == 0
                    ? "no specimens"
                    : $"created {_inspection.EarliestCreatedDate} to {_inspection.LatestCreatedDate}";
                SummaryText.Text =
                    $"{_inspection.SpecimenCount} specimen(s) ({range}), " +
                    $"{_inspection.PanelCount} panel(s), " +
                    $"{DatabaseCapacityStatus.FormatBytes(_inspection.FileBytes)}. " +
                    $"{_inspection.RestorableCount} can be restored; " +
                    $"{_inspection.AlreadyInLiveCount} already in the live database.";
            }
            catch (Exception ex)
            {
                _inspection = null;
                SpecimenGrid.ItemsSource = null;
                RestoreButton.IsEnabled = false;
                SummaryText.Text = "";
                PathBox.Text = path;
                MessageBox.Show(ex.Message, "Open Archive", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RestoreClick(object sender, RoutedEventArgs e)
        {
            if (_inspection == null || _inspection.RestorableCount <= 0) return;

            var confirm = MessageBox.Show(
                $"Restore {_inspection.RestorableCount} specimen(s) from this archive into the live database?\n\n" +
                "Accessions that already exist will be skipped. Archives contain specimen records.",
                "Confirm restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                RestoreResult = _db.RestoreArchive(_inspection.Path);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
