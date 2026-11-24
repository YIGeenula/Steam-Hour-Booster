using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SteamPulse.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush GreenBrush = new((Color)ColorConverter.ConvertFromString("#10B981"));
        private static readonly SolidColorBrush MutedBrush = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isBoosting = value is bool b && b;
            return isBoosting ? GreenBrush : MutedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool val = value is bool b && b;
            if (Invert) val = !val;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToActionBrushConverter : IValueConverter
    {
        // Solid action brushes - NO GRADIENTS
        private static readonly SolidColorBrush StopBrush = new((Color)ColorConverter.ConvertFromString("#DC2626"));
        private static readonly SolidColorBrush PrimaryBlueBrush = new((Color)ColorConverter.ConvertFromString("#2563EB"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isBoosting = value is bool b && b;
            return isBoosting ? StopBrush : PrimaryBlueBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
