using System;
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
    public class AnalyticsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db;
        private readonly MainViewModel? _main;

        public ObservableCollection<AntibodyCountRow> Rows { get; } = new();
        public ObservableCollection<AntibodyTrendPoint> Trend { get; } = new();

        public ICommand Last30DaysCommand { get; }
        public ICommand Last90DaysCommand { get; }
        public ICommand Last12MonthsCommand { get; }
        public ICommand AllTimeCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public event EventHandler? ChartDataChanged;

        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set { if (SetField(ref _fromDate, value)) Recompute(); }
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set { if (SetField(ref _toDate, value)) Recompute(); }
        }

        private int _confirmedSpecimens;
        public int ConfirmedSpecimens
        {
            get => _confirmedSpecimens;
            private set => SetField(ref _confirmedSpecimens, value);
        }

        private int _distinctSpecificities;
        public int DistinctSpecificities
        {
            get => _distinctSpecificities;
            private set => SetField(ref _distinctSpecificities, value);
        }

        private int _totalOccurrences;
        public int TotalOccurrences
        {
            get => _totalOccurrences;
            private set => SetField(ref _totalOccurrences, value);
        }

        private string _mostCommonId = "—";
        public string MostCommonId
        {
            get => _mostCommonId;
            private set => SetField(ref _mostCommonId, value);
        }

        public bool IsAllTime => FromDate == null && ToDate == null;

        public string RangeLabel =>
            IsAllTime
                ? "All time"
                : $"{FromDate?.ToString("yyyy-MM-dd") ?? "…"} to {ToDate?.ToString("yyyy-MM-dd") ?? "…"}";

        public AnalyticsViewModel(DatabaseService db, MainViewModel? main = null)
        {
            _db = db;
            _main = main;
            Last30DaysCommand = new RelayCommand(() => ApplyRange(DateTime.Today.AddDays(-30), DateTime.Today));
            Last90DaysCommand = new RelayCommand(() => ApplyRange(DateTime.Today.AddDays(-90), DateTime.Today));
            Last12MonthsCommand = new RelayCommand(() => ApplyRange(DateTime.Today.AddYears(-1), DateTime.Today));
            AllTimeCommand = new RelayCommand(() => ApplyRange(null, null));
            RefreshCommand = new RelayCommand(Refresh);
            ExportCsvCommand = new RelayCommand(ExportCsv);
            _fromDate = DateTime.Today.AddYears(-1);
            _toDate = DateTime.Today;
            Recompute();
        }

        public void Refresh() => Recompute();

        private void ApplyRange(DateTime? from, DateTime? to)
        {
            _fromDate = from;
            _toDate = to;
            OnPropertyChanged(nameof(FromDate));
            OnPropertyChanged(nameof(ToDate));
            Recompute();
        }

        private void Recompute()
        {
            var result = AntibodyAnalyticsService.Compute(_db.GetAllSpecimens(), FromDate, ToDate);

            ConfirmedSpecimens = result.ConfirmedSpecimens;
            DistinctSpecificities = result.DistinctSpecificities;
            TotalOccurrences = result.TotalOccurrences;
            MostCommonId = result.MostCommonId;

            Rows.Clear();
            foreach (var row in result.Rows)
                Rows.Add(row);

            Trend.Clear();
            foreach (var point in result.Trend)
                Trend.Add(point);

            OnPropertyChanged(nameof(IsAllTime));
            OnPropertyChanged(nameof(RangeLabel));
            ChartDataChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog { Filter = "CSV Files|*.csv", DefaultExt = "csv" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var result = new AntibodyAnalyticsResult
                {
                    ConfirmedSpecimens = ConfirmedSpecimens,
                    DistinctSpecificities = DistinctSpecificities,
                    TotalOccurrences = TotalOccurrences,
                    MostCommonId = MostCommonId,
                    Rows = Rows.ToList(),
                    Trend = Trend.ToList()
                };
                AntibodyAnalyticsService.ExportToCsv(result, FromDate, ToDate, dlg.FileName);
                _main?.SetStatus($"CSV exported: {dlg.FileName}");
                MessageBox.Show("CSV exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
