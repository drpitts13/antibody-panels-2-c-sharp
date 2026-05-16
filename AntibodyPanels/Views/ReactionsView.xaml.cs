using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AntibodyPanels.Models;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    /// <summary>
    /// Bindable header object for each antigen column.
    /// Setting IsRuledOut=true turns the header text red via a DataTemplate trigger.
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

        public AntigenColumnHeader(string antigen) => Antigen = antigen;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ReactionsView : UserControl
    {
        private bool _columnsInjected;
        private readonly Dictionary<string, AntigenColumnHeader> _antigenHeaders = new();
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
            if (_vm != null) _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReactionsViewModel.RuledOutAntigens))
                ApplyRuledOutToHeaders();
        }

        private void ApplyRuledOutToHeaders()
        {
            if (_vm == null) return;
            foreach (var (ag, header) in _antigenHeaders)
                header.IsRuledOut = _vm.RuledOutAntigens.Contains(ag);
        }

        private void ReactionsGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_columnsInjected) return;
            _columnsInjected = true;

            var headerTemplate = (DataTemplate)FindResource("AntigenHeaderTemplate");
            var positiveBg = new SolidColorBrush(Color.FromRgb(200, 230, 201));
            var centeredText = new Style(typeof(TextBlock));
            centeredText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            centeredText.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));

            // Insert antigen columns at index 1 (after Cell, before IS/37/AHG/CC)
            int insertIdx = 1;
            foreach (var ag in AntigenConstants.Antigens)
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

                ReactionsGrid.Columns.Insert(insertIdx++, new DataGridTextColumn
                {
                    Header = header,
                    HeaderTemplate = headerTemplate,
                    Width = 38,
                    IsReadOnly = true,
                    Binding = new Binding($"AntigenValues[{ag}]"),
                    ElementStyle = centeredText,
                    CellStyle = cellStyle,
                });
            }

            // Ruled Out column — appended after CC
            var ruledOutStyle = new Style(typeof(TextBlock));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(46, 125, 50))));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            ruledOutStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));

            ReactionsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Ruled Out",
                Width = DataGridLength.Auto,
                MinWidth = 120,
                IsReadOnly = true,
                Binding = new Binding("RuledOutNote"),
                ElementStyle = ruledOutStyle,
            });

            // Apply initial state if a specimen is already selected
            ApplyRuledOutToHeaders();
        }
    }
}
