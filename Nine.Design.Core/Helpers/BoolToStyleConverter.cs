using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Nine.Design.Core
{
    /// <summary>
    /// 布尔值转样式转换器
    /// </summary>
    public class BoolToStyleConverter : IValueConverter
    {
        public Style TrueStyle { get; set; }
        public Style FalseStyle { get; set; }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? TrueStyle : FalseStyle;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值转命令转换器
    /// </summary>
    public class BoolToCommandConverter : IValueConverter
    {
        public ICommand TrueCommand { get; set; }
        public ICommand FalseCommand { get; set; }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? TrueCommand : FalseCommand;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}