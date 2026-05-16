using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AntibodyPanels.Data;
using AntibodyPanels.Models;
using AntibodyPanels.Services;

namespace AntibodyPanels.ViewModels
{
    public class ReportsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly ReportService _reportService;
        private readonly MainViewModel _main;

        public ObservableCollection<Specimen> Specimens { get; } = new();
        public ObservableCollection<Panel> Panels { get; } = new();

        public ObservableCollection<string> ReportTypes { get; } = new()
        {
            "Specimen Summary",
            "Panel Summary",
            "Analysis Results",
            "All Specimens",
            "All Panels"
        };

        private string _selectedReportType = "All Specimens";
        public string SelectedReportType
        {
            get => _selectedReportType;
            set { SetField(ref _selectedReportType, value); UpdatePreview(); }
        }

        private Specimen? _selectedSpecimen;
        public Specimen? SelectedSpecimen
        {
            get => _selectedSpecimen;
            set { SetField(ref _selectedSpecimen, value); UpdatePreview(); }
        }

        private Panel? _selectedPanel;
        public Panel? SelectedPanel
        {
            get => _selectedPanel;
            set { SetField(ref _selectedPanel, value); UpdatePreview(); }
        }

        private string _previewText = string.Empty;
        public string PreviewText
        {
            get => _previewText;
            set => SetField(ref _previewText, value);
        }

        public bool NeedsSpecimen => SelectedReportType is
            "Specimen Summary" or "Analysis Results";

        public bool NeedsPanel => SelectedReportType == "Panel Summary";

        public ICommand ExportCsvCommand { get; }
        public ICommand ExportPdfCommand { get; }

        public ReportsViewModel(DatabaseService db, MainViewModel main)
        {
            _db = db;
            _main = main;
            _reportService = new ReportService(db);

            ExportCsvCommand = new RelayCommand(ExportCsv);
            ExportPdfCommand = new RelayCommand(ExportPdf);

            foreach (var s in _db.GetAllSpecimens()) Specimens.Add(s);
            foreach (var p in _db.GetAllPanels()) Panels.Add(p);
            SelectedSpecimen = Specimens.FirstOrDefault();
            SelectedPanel = Panels.FirstOrDefault();
            UpdatePreview();
        }

        public void Refresh()
        {
            var selSpecimen = SelectedSpecimen?.AccessionNumber;
            var selPanel = SelectedPanel?.PanelId;

            Specimens.Clear();
            foreach (var s in _db.GetAllSpecimens()) Specimens.Add(s);
            SelectedSpecimen = selSpecimen != null
                ? Specimens.FirstOrDefault(s => s.AccessionNumber == selSpecimen) ?? Specimens.FirstOrDefault()
                : Specimens.FirstOrDefault();

            Panels.Clear();
            foreach (var p in _db.GetAllPanels()) Panels.Add(p);
            SelectedPanel = selPanel != null
                ? Panels.FirstOrDefault(p => p.PanelId == selPanel) ?? Panels.FirstOrDefault()
                : Panels.FirstOrDefault();

            UpdatePreview();
        }

        private ReportType GetReportType() => SelectedReportType switch
        {
            "Specimen Summary" => ReportType.SpecimenSummary,
            "Panel Summary" => ReportType.PanelSummary,
            "Analysis Results" => ReportType.AnalysisResults,
            "All Panels" => ReportType.AllPanels,
            _ => ReportType.AllSpecimens
        };

        private void UpdatePreview()
        {
            OnPropertyChanged(nameof(NeedsSpecimen));
            OnPropertyChanged(nameof(NeedsPanel));
            try
            {
                PreviewText = _reportService.GeneratePreviewText(
                    GetReportType(),
                    SelectedSpecimen?.AccessionNumber,
                    SelectedPanel?.PanelId);
            }
            catch (System.Exception ex)
            {
                PreviewText = $"Error generating report: {ex.Message}";
            }
        }

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog { Filter = "CSV Files|*.csv", DefaultExt = "csv" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _reportService.ExportToCsv(GetReportType(), dlg.FileName,
                    SelectedSpecimen?.AccessionNumber, SelectedPanel?.PanelId);
                _main.SetStatus($"CSV exported: {dlg.FileName}");
                MessageBox.Show("CSV exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPdf()
        {
            var dlg = new SaveFileDialog { Filter = "PDF Files|*.pdf", DefaultExt = "pdf" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _reportService.ExportToPdf(GetReportType(), dlg.FileName,
                    SelectedSpecimen?.AccessionNumber, SelectedPanel?.PanelId);
                _main.SetStatus($"PDF exported: {dlg.FileName}");
                MessageBox.Show("PDF exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
