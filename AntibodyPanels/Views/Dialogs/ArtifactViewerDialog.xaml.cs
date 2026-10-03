using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AntibodyPanels.Services;

namespace AntibodyPanels.Views.Dialogs
{
    public partial class ArtifactViewerDialog : Window
    {
        private readonly ArtifactInspection _inspection;

        public ArtifactViewerDialog(ArtifactInspection inspection)
        {
            InitializeComponent();
            _inspection = inspection;
            StatusBox.Text = inspection.Explanation;
            HashBox.Text = inspection.ActualSha256 ?? inspection.ExpectedSha256 ?? "—";
            PathBox.Text = inspection.Path ?? "—";
            PreviewBox.Text = string.IsNullOrWhiteSpace(inspection.TextPreview)
                ? "(No text preview. Use Open file for PDF or other binary artifacts.)"
                : inspection.TextPreview;
            OpenFileButton.IsEnabled = inspection.CanOpen;
            OpenFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(inspection.Path);
        }

        private void OpenFileClick(object sender, RoutedEventArgs e)
        {
            if (!_inspection.CanOpen || string.IsNullOrWhiteSpace(_inspection.Path)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = _inspection.Path,
                UseShellExecute = true
            });
        }

        private void OpenFolderClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_inspection.Path)) return;
            var folder = File.Exists(_inspection.Path)
                ? Path.GetDirectoryName(_inspection.Path)
                : _inspection.Path;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        private void CopyHashClick(object sender, RoutedEventArgs e)
        {
            var hash = _inspection.ActualSha256 ?? _inspection.ExpectedSha256;
            if (string.IsNullOrWhiteSpace(hash)) return;
            Clipboard.SetText(hash);
        }
    }
}
