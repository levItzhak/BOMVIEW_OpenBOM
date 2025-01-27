using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BOMVIEW
{
    public class SupplierToColorConverter : IValueConverter
    {
        public SolidColorBrush DigiKeyBrush { get; set; }
        public SolidColorBrush MouserBrush { get; set; }
        public SolidColorBrush FarnellBrush { get; set; }
        public SolidColorBrush DefaultBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string supplier)
            {
                switch (supplier)
                {
                    case "DigiKey":
                        return DigiKeyBrush;
                    case "Mouser":
                        return MouserBrush;
                    case "Farnell":
                        return FarnellBrush;
                    default:
                        return DefaultBrush;
                }
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}