using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AddInManager.Wpf
{
    public class LeftMarginMultiplier : IValueConverter
    {
        public double Length { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var item = value as TreeViewItem;
            if (item == null)
                return new Thickness(0);

            return new Thickness(GetDepth(item) * 12, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private int GetDepth(TreeViewItem item)
        {
            var depth = 0;
            while ((item = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem) != null)
            {
                depth++;
            }
            return depth;
        }
    }
}
