using System;
using System.Windows;
using System.Windows.Data;
using BOMVIEW.Models;

namespace BOMVIEW.Views
{
    public class NodeTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is BomTreeNode.NodeType nodeType)
            {
                return nodeType == BomTreeNode.NodeType.Item ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}