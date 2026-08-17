using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AntibodyPanels.Models;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    public partial class PanelsView : UserControl
    {
        public static readonly string[] AntigenValues = { "+", "-" };

        private bool _columnsInjected;
        private PanelsViewModel? _vm;
        private readonly List<DataGridColumn> _extraColumns = new();

        public PanelsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.ExtraAntigens.CollectionChanged -= OnExtraAntigensChanged;
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _vm = e.NewValue as PanelsViewModel;
            if (_vm != null)
            {
                _vm.ExtraAntigens.CollectionChanged += OnExtraAntigensChanged;
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                RebuildExtraColumns();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PanelsViewModel.SelectedPanel))
                RebuildExtraColumns();
        }

        private void OnExtraAntigensChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            RebuildExtraColumns();

        private void AntigenGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_columnsInjected) return;
            _columnsInjected = true;

            var centeredText = new Style(typeof(TextBlock));
            centeredText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            centeredText.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));

            foreach (var ag in AntigenConstants.Antigens)
            {
                AntigenGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = ag,
                    Width = ag.Length >= 3 ? 45 : 40,
                    IsReadOnly = true,
                    Binding = new Binding(ag),
                    ElementStyle = centeredText,
                    CellStyle = CreateAntigenCellStyle(ag, namedProperty: true),
                });
            }

            RebuildExtraColumns();
        }

        private void RebuildExtraColumns()
        {
            if (!_columnsInjected) return;

            foreach (var col in _extraColumns)
                AntigenGrid.Columns.Remove(col);
            _extraColumns.Clear();

            if (_vm == null) return;

            var centeredText = new Style(typeof(TextBlock));
            centeredText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            centeredText.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));

            foreach (var ag in _vm.ExtraAntigens)
            {
                var col = new DataGridTextColumn
                {
                    Header = ag,
                    Width = ag.Length >= 3 ? 48 : 42,
                    IsReadOnly = true,
                    Binding = new Binding($"AntigenValues[{ag}]"),
                    ElementStyle = centeredText,
                    CellStyle = CreateAntigenCellStyle(ag, namedProperty: false),
                };
                AntigenGrid.Columns.Add(col);
                _extraColumns.Add(col);
            }
        }

        private Style CreateAntigenCellStyle(string antigen, bool namedProperty)
        {
            var path = namedProperty ? antigen : $"AntigenValues[{antigen}]";
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(ForegroundProperty, Brush("AntigenCellTextBrush")));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

            var plus = new DataTrigger { Binding = new Binding(path), Value = "+" };
            plus.Setters.Add(new Setter(BackgroundProperty, Brush("AntigenPositiveBrush")));
            style.Triggers.Add(plus);

            var minus = new DataTrigger { Binding = new Binding(path), Value = "-" };
            minus.Setters.Add(new Setter(BackgroundProperty, Brush("AntigenNegativeBrush")));
            style.Triggers.Add(minus);

            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(BorderBrushProperty, Brush("AntigenSelectedBorderBrush")));
            selected.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
            style.Triggers.Add(selected);

            return style;
        }

        private Brush Brush(string key) => (Brush)FindResource(key);

        private void AntigenGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1) { e.Handled = true; return; }

            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell?.Column?.Header is not string antigen || !IsAntigenColumn(antigen))
                return;
            if (cell.DataContext is not PanelCellRow row)
                return;

            AntigenGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
            AntigenGrid.SelectedCells.Clear();
            AntigenGrid.SelectedCells.Add(AntigenGrid.CurrentCell);

            if (DataContext is PanelsViewModel vm && vm.IsEditingAntigens)
                row.ToggleAntigen(antigen);
            e.Handled = true;
        }

        private void AntigenGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Return or Key.Space))
                return;
            if (ToggleCurrentCell())
                e.Handled = true;
        }

        private bool ToggleCurrentCell()
        {
            var current = AntigenGrid.CurrentCell;
            if (current.Item is not PanelCellRow row)
                return false;
            if (current.Column?.Header is not string antigen || !IsAntigenColumn(antigen))
                return false;
            if (DataContext is not PanelsViewModel vm || !vm.IsEditingAntigens)
                return false;

            row.ToggleAntigen(antigen);
            return true;
        }

        private static bool IsAntigenColumn(string header) =>
            header != "Cell" && AntigenConstants.IsKnown(header);

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
