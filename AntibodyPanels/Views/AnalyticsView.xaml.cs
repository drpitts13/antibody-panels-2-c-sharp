using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AntibodyPanels.ViewModels;
using ScottPlot;
using ScottPlot.TickGenerators;

namespace AntibodyPanels.Views
{
    public partial class AnalyticsView : UserControl
    {
        private AnalyticsViewModel? _vm;

        public AnalyticsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => RefreshPlots();
            Unloaded += (_, _) => UnhookVm();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UnhookVm();
            _vm = DataContext as AnalyticsViewModel;
            if (_vm != null)
                _vm.ChartDataChanged += OnChartDataChanged;
            RefreshPlots();
        }

        private void UnhookVm()
        {
            if (_vm == null) return;
            _vm.ChartDataChanged -= OnChartDataChanged;
            _vm = null;
        }

        private void OnChartDataChanged(object? sender, EventArgs e) => RefreshPlots();

        private void RefreshPlots()
        {
            if (BarPlot == null || TrendPlot == null) return;
            RenderBarChart();
            RenderTrendChart();
        }

        private void RenderBarChart()
        {
            var plot = BarPlot.Plot;
            plot.Clear();
            plot.Title("Count by antibody");
            plot.Axes.Bottom.Label.Text = "Count";
            plot.FigureBackground.Color = Colors.White;

            var rows = _vm?.Rows.ToList() ?? new();
            if (rows.Count == 0)
            {
                plot.Title("No confirmed identifications in this range");
                BarPlot.Refresh();
                return;
            }

            var bars = rows.Select((row, i) => new Bar
            {
                Position = rows.Count - 1 - i,
                Value = row.Count,
                FillColor = Color.FromHex("#1565C0")
            }).ToList();
            var barPlot = plot.Add.Bars(bars);
            barPlot.Horizontal = true;

            var ticks = rows
                .Select((row, i) => new Tick(rows.Count - 1 - i, row.Antibody))
                .ToArray();
            plot.Axes.Left.TickGenerator = new NumericManual(ticks);
            plot.Axes.Bottom.Min = 0;
            plot.Axes.Margins(left: 0.02, right: 0.12, bottom: 0.05, top: 0.05);
            BarPlot.Refresh();
        }

        private void RenderTrendChart()
        {
            var plot = TrendPlot.Plot;
            plot.Clear();
            plot.Title("Confirmed specimens by month");
            plot.Axes.Left.Label.Text = "Specimens";
            plot.FigureBackground.Color = Colors.White;

            var points = _vm?.Trend.ToList() ?? new();
            if (points.Count == 0)
            {
                plot.Title("No monthly trend for this range");
                TrendPlot.Refresh();
                return;
            }

            var bars = points.Select((p, i) => new Bar
            {
                Position = i,
                Value = p.SpecimenCount,
                FillColor = Color.FromHex("#00838F")
            }).ToList();
            plot.Add.Bars(bars);

            var ticks = points.Select((p, i) => new Tick(i, p.MonthLabel)).ToArray();
            plot.Axes.Bottom.TickGenerator = new NumericManual(ticks);
            plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
            plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.UpperRight;
            plot.Axes.Left.Min = 0;
            plot.Axes.Margins(left: 0.05, right: 0.05, bottom: 0.2, top: 0.05);
            TrendPlot.Refresh();
        }
    }
}
