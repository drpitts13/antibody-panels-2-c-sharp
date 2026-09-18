using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace AntibodyPanels.Views
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }

    public class InverseBoolConverter : IValueConverter
    {
        public static readonly InverseBoolConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;
    }

    /// <summary>
    /// True when <paramref name="parameter"/> is present in a string collection.
    /// Used to show a slash overlay on a specific antigen column.
    /// </summary>
    public class CollectionContainsToVisibilityConverter : IValueConverter
    {
        public static readonly CollectionContainsToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string item || string.IsNullOrEmpty(item))
                return Visibility.Collapsed;
            if (value is IEnumerable<string> items && items.Contains(item))
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
