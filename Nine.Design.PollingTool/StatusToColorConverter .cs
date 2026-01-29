using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Nine.Design.PollingTool
{
    public class ScaleConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 获取基准字体大小
            if (double.TryParse(parameter.ToString(), out double baseSize))
            {
                // 获取窗口当前字体大小（作为缩放基准）
                double windowFontSize = (double)value;

                // 计算缩放比例（默认窗口字体大小为12，以此为基准）
                double scale = windowFontSize / 12;

                // 返回缩放后的字体大小
                return baseSize * scale;
            }
            return 12; // 默认值
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
    public class StatusToColorConverter : IValueConverter
    {
        // 缩放转换器 - 根据窗口大小调整字体尺寸
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                //return status switch
                //{
                //    "运行中" => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                //    "已停止" => new SolidColorBrush(Color.FromRgb(155, 89, 182)),
                //    "错误" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                //    _ => new SolidColorBrush(Color.FromRgb(127, 140, 141))
                //};
                GetStatusColorBrush(status);
            }
            return new SolidColorBrush(Color.FromRgb(127, 140, 141));
        }
        // 定义方法，参数status为传入的状态字符串
        public SolidColorBrush GetStatusColorBrush(string status)
        {
            // 传统switch语句（C# 7.3支持字符串case匹配）
            switch (status)
            {
                case "运行中":
                    return new SolidColorBrush(Color.FromRgb(46, 204, 113));
                case "已停止":
                    return new SolidColorBrush(Color.FromRgb(155, 89, 182));
                case "错误":
                    return new SolidColorBrush(Color.FromRgb(231, 76, 60));
                // default匹配所有未明确指定的情况（对应原代码的_）
                default:
                    return new SolidColorBrush(Color.FromRgb(127, 140, 141));
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }


    }
}