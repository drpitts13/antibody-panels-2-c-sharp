using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AntibodyPanels.ViewModels;

namespace AntibodyPanels.Views
{
    /// <summary>
    /// Antigen +/− mark with a diagonal slash that tracks the row's live grades.
    /// Subscribes to <see cref="ReactionRow"/> so a correction from 0 to a
    /// positive grade removes the slash immediately.
    /// </summary>
    public class AntigenSlashCell : Grid
    {
        public static readonly DependencyProperty AntigenProperty =
            DependencyProperty.Register(
                nameof(Antigen),
                typeof(string),
                typeof(AntigenSlashCell),
                new PropertyMetadata(null, (d, _) => ((AntigenSlashCell)d).Apply()));

        private readonly TextBlock _text;
        private readonly Line _slash;
        private ReactionRow? _row;

        public string? Antigen
        {
            get => (string?)GetValue(AntigenProperty);
            set => SetValue(AntigenProperty, value);
        }

        public AntigenSlashCell()
        {
            ClipToBounds = true;
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            _text = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 13.75,
                FontWeight = FontWeights.SemiBold
            };
            Children.Add(_text);

            _slash = new Line
            {
                X1 = 0,
                Y1 = 1,
                X2 = 1,
                Y2 = 0,
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(Color.FromRgb(33, 33, 33)),
                StrokeThickness = 1.4,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Margin = new Thickness(1),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_slash);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Attach(e.NewValue as ReactionRow);
            Apply();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Attach(DataContext as ReactionRow);
            Apply();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Attach(ReactionRow? row)
        {
            if (ReferenceEquals(_row, row)) return;
            Detach();
            _row = row;
            if (_row != null)
                _row.PropertyChanged += OnRowPropertyChanged;
        }

        private void Detach()
        {
            if (_row == null) return;
            _row.PropertyChanged -= OnRowPropertyChanged;
            _row = null;
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null
                or nameof(ReactionRow.SlashedAntigens)
                or nameof(ReactionRow.IsNegative)
                or nameof(ReactionRow.HasRuleout)
                or nameof(ReactionRow.IS)
                or nameof(ReactionRow.C37)
                or nameof(ReactionRow.AHG))
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (_row == null || string.IsNullOrEmpty(Antigen))
            {
                _text.Text = string.Empty;
                _slash.Visibility = Visibility.Collapsed;
                return;
            }

            _text.Text = _row.AntigenValues.TryGetValue(Antigen, out var value) ? value : string.Empty;
            _slash.Visibility = _row.SlashedAntigens.Contains(Antigen)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
