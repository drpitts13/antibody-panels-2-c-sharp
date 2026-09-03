using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    /// <summary>
    /// Bindable header object for each antigen column.
    /// IsRuledOut colours the header red; IsDestroyed greys it out with strikethrough.
    /// </summary>
    public class AntigenColumnHeader : INotifyPropertyChanged
    {
        public string Antigen { get; }

        private bool _isRuledOut;
        public bool IsRuledOut
        {
            get => _isRuledOut;
            set { _isRuledOut = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRuledOut))); }
        }

        private bool _isDestroyed;
        public bool IsDestroyed
        {
            get => _isDestroyed;
            set { _isDestroyed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDestroyed))); }
        }

        public AntigenColumnHeader(string antigen) => Antigen = antigen;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ReactionsView : UserControl
    {
        private bool _columnsInjected;
        private bool _ruledOutColumnAdded;
        private readonly Dictionary<string, AntigenColumnHeader> _antigenHeaders = new();
        private readonly Dictionary<string, DataGridColumn> _antigenColumns = new();
        private readonly List<string> _dynamicAntigenNames = new();
        private ReactionsViewModel? _vm;

        public ReactionsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = e.NewValue as ReactionsViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnViewModelPropertyChanged;
                RebuildAntigenColumns();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReactionsViewModel.RuledOutAntigens) ||
                e.PropertyName == nameof(ReactionsViewModel.HideRuledOutAntigenColumns))
            {
                ApplyRuledOutToHeaders();
                ApplyAntigenColumnVisibility();
            }
            else if (e.PropertyName == nameof(ReactionsViewModel.DestroyedAntigens))
                ApplyDestroyedToHeaders();
            else if (e.PropertyName == nameof(ReactionsViewModel.AntigenDisplayOrder) ||
                     e.PropertyName == nameof(ReactionsViewModel.ExtraAntigens))
                RebuildAntigenColumns();
        }

        private void ApplyRuledOutToHeaders()
        {
            if (_vm == null) return;
            foreach (var (ag, header) in _antigenHeaders)
                header.IsRuledOut = _vm.RuledOutAntigens.Contains(ag);
        }

        private void ApplyDestroyedToHeaders()
        {
            if (_vm == null) return;
            foreach (var (ag, header) in _antigenHeaders)
                header.IsDestroyed = _vm.DestroyedAntigens.Contains(ag);
        }

        private void ApplyAntigenColumnVisibility()
        {
            if (_vm == null) return;
            bool hide = _vm.HideRuledOutAntigenColumns;
            foreach (var (ag, col) in _antigenColumns)
            {
                bool ruledOut = _vm.RuledOutAntigens.Contains(ag);
                col.Visibility = hide && ruledOut ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ReactionsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_columnsInjected) return;
            _columnsInjected = true;

            RebuildAntigenColumns();
            EnsureRuledOutColumn();

            ApplyGradeStylesToPhaseColumns();
            ApplyCompareGradeStyles();
            ApplyRuledOutToHeaders();
            ApplyDestroyedToHeaders();
            ApplyAntigenColumnVisibility();
        }

        private void RebuildAntigenColumns()
        {
            if (!_columnsInjected) return;

            foreach (var name in _dynamicAntigenNames)
            {
                if (_antigenColumns.TryGetValue(name, out var col))
                    ReactionsGrid.Columns.Remove(col);
                _antigenColumns.Remove(name);
                _antigenHeaders.Remove(name);
            }
            _dynamicAntigenNames.Clear();

            if (_vm == null) return;

            var headerTemplate = (DataTemplate)FindResource("AntigenHeaderTemplate");
            var positiveBg = new SolidColorBrush(Color.FromRgb(200, 230, 201));
            var centeredText = new Style(typeof(TextBlock));
            centeredText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            centeredText.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));

            int insertIdx = 1;
            foreach (var ag in _vm.AntigenDisplayOrder)
            {
                var header = new AntigenColumnHeader(ag);
                _antigenHeaders[ag] = header;

                var cellStyle = new Style(typeof(DataGridCell));
                var posTrigger = new DataTrigger
                {
                    Binding = new Binding($"AntigenValues[{ag}]"),
                    Value = "+"
                };
                posTrigger.Setters.Add(new Setter(BackgroundProperty, positiveBg));
                cellStyle.Triggers.Add(posTrigger);

                var col = new DataGridTextColumn
                {
                    Header = header,
                    HeaderTemplate = headerTemplate,
                    Width = ag.Length >= 3 ? 42 : 38,
                    IsReadOnly = true,
                    Binding = new Binding($"AntigenValues[{ag}]"),
                    ElementStyle = centeredText,
                    CellStyle = cellStyle,
                };
                ReactionsGrid.Columns.Insert(insertIdx++, col);
                _antigenColumns[ag] = col;
                _dynamicAntigenNames.Add(ag);
            }

            ApplyRuledOutToHeaders();
            ApplyDestroyedToHeaders();
            ApplyAntigenColumnVisibility();
        }

        private void EnsureRuledOutColumn()
        {
            if (_ruledOutColumnAdded) return;
            _ruledOutColumnAdded = true;

            var ruledOutStyle = new Style(typeof(TextBlock));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(46, 125, 50))));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));

            var ruledOutCol = new DataGridTextColumn
            {
                Header = "Ruled Out",
                Width = DataGridLength.Auto,
                MinWidth = 120,
                IsReadOnly = true,
                Binding = new Binding("RuledOutNote"),
                ElementStyle = ruledOutStyle,
            };
            ReactionsGrid.Columns.Add(ruledOutCol);
        }

        private void ApplyGradeStylesToPhaseColumns()
        {
            foreach (var col in ReactionsGrid.Columns)
            {
                var header = col.Header as string;
                if (header == "IS") col.CellStyle = CreateGradeCellStyle("IS");
                else if (header == "37°C") col.CellStyle = CreateGradeCellStyle("C37");
                else if (header == "AHG") col.CellStyle = CreateGradeCellStyle("AHG");
                else if (header == "CC") col.CellStyle = CreateGradeCellStyle("CC", ccColumn: true);
            }
        }

        private void ApplyCompareGradeStyles()
        {
            foreach (var col in CompareGrid.Columns)
            {
                if (col.Header is not string header) continue;
                col.CellStyle = header switch
                {
                    "This IS" => CreateGradeCellStyle("LeftIS"),
                    "Other IS" => CreateGradeCellStyle("RightIS"),
                    "This 37°C" => CreateGradeCellStyle("LeftC37"),
                    "Other 37°C" => CreateGradeCellStyle("RightC37"),
                    "This AHG" => CreateGradeCellStyle("LeftAHG"),
                    "Other AHG" => CreateGradeCellStyle("RightAHG"),
                    "This CC" => CreateGradeCellStyle("LeftCC"),
                    "Other CC" => CreateGradeCellStyle("RightCC"),
                    _ => col.CellStyle
                };
            }
        }

        private static Style CreateGradeCellStyle(string property, bool ccColumn = false)
        {
            var style = new Style(typeof(DataGridCell));
            AddGrade(style, property, "0", Color.FromRgb(255, 255, 255), Colors.Black);
            AddGrade(style, property, "1+", Color.FromRgb(255, 249, 196), Colors.Black);
            AddGrade(style, property, "2+", Color.FromRgb(255, 183, 77), Colors.Black);
            AddGrade(style, property, "3+", Color.FromRgb(229, 57, 53), Colors.White);
            AddGrade(style, property, "4+", Color.FromRgb(183, 28, 28), Colors.White);
            AddGrade(style, property, "NT", Color.FromRgb(238, 238, 238), Color.FromRgb(97, 97, 97));
            if (ccColumn)
            {
                var invalid = new DataTrigger { Binding = new Binding("IsCcInvalid"), Value = true };
                invalid.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 205, 210))));
                invalid.Setters.Add(new Setter(ToolTipProperty, "Check cells should react when AHG is 0"));
                style.Triggers.Add(invalid);
            }
            return style;
        }

        private static void AddGrade(Style style, string property, string grade, Color bg, Color fg)
        {
            var trigger = new DataTrigger { Binding = new Binding(property), Value = grade };
            trigger.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(bg)));
            trigger.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(fg)));
            trigger.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(trigger);
        }

        private void ReactionsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ReactionsGrid.CurrentItem is not ReactionRow row) return;
            var header = ReactionsGrid.CurrentColumn?.Header as string;
            if (header is not ("IS" or "37°C" or "AHG" or "CC")) return;

            string? grade = e.Key switch
            {
                Key.D0 or Key.NumPad0 => "0",
                Key.D1 or Key.NumPad1 => "1+",
                Key.D2 or Key.NumPad2 => "2+",
                Key.D3 or Key.NumPad3 => "3+",
                Key.D4 or Key.NumPad4 => "4+",
                Key.N => "NT",
                _ => null
            };

            if (grade != null)
            {
                SetPhase(row, header, grade);
                MoveToNextPhase();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Return)
            {
                MoveToNextPhase();
                e.Handled = true;
            }
        }

        private static void SetPhase(ReactionRow row, string header, string grade)
        {
            switch (header)
            {
                case "IS": row.IS = grade; break;
                case "37°C": row.C37 = grade; break;
                case "AHG": row.AHG = grade; break;
                case "CC": row.CC = grade; break;
            }
        }

        private void MoveToNextPhase()
        {
            var current = ReactionsGrid.CurrentColumn;
            var item = ReactionsGrid.CurrentItem;
            if (current == null || item == null) return;

            string[] order = { "IS", "37°C", "AHG", "CC" };
            int idx = System.Array.IndexOf(order, current.Header as string);
            if (idx < 0) return;

            if (idx < order.Length - 1)
            {
                var next = FindColumn(order[idx + 1]);
                if (next != null)
                    ReactionsGrid.CurrentCell = new DataGridCellInfo(item, next);
                return;
            }

            int rowIndex = ReactionsGrid.Items.IndexOf(item);
            if (rowIndex >= 0 && rowIndex < ReactionsGrid.Items.Count - 1)
            {
                var nextItem = ReactionsGrid.Items[rowIndex + 1];
                var isCol = FindColumn("IS");
                if (isCol != null)
                    ReactionsGrid.CurrentCell = new DataGridCellInfo(nextItem, isCol);
            }
        }

        private DataGridColumn? FindColumn(string header)
        {
            foreach (var col in ReactionsGrid.Columns)
                if (col.Header as string == header) return col;
            return null;
        }
    }
}
