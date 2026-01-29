using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Nine.Design.PollingTool
{
    #region 单个值转换器（IValueConverter）
    /// <summary>
    /// 字体大小缩放转换器（基于窗口基准字体大小12进行缩放）
    /// 继承MarkupExtension，支持XAML内联直接使用
    /// </summary>
    public class ScaleConverter : MarkupExtension, IValueConverter
    {
        // 单例实例，提升XAML使用时的性能
        private static ScaleConverter _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 双重校验：参数不为null + 能解析为双精度浮点数
            if (parameter != null && double.TryParse(parameter.ToString(), out double baseSize) && value is double windowFontSize)
            {
                // 计算缩放比例并返回最终字体大小
                double scale = windowFontSize / 12;
                return baseSize * scale;
            }

            // 默认返回基础字体大小12
            return 12.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 反向转换未实现（无需使用）
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 懒加载创建单例实例
            return _instance ?? (_instance = new ScaleConverter());
        }
    }

    /// <summary>
    /// 状态字符串转颜色转换器（匹配"运行中"/"已停止"/"错误"三种状态）
    /// 继承MarkupExtension，支持XAML内联直接使用
    /// </summary>
    public class StatusToColorConverter : MarkupExtension, IValueConverter
    {
        // 单例实例，提升XAML使用时的性能
        private static StatusToColorConverter _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 校验值为有效字符串状态，调用方法并返回结果（修复核心逻辑：添加return）
            if (value is string status)
            {
                return GetStatusColorBrush(status);
            }

            // 默认返回浅灰色画刷
            return new SolidColorBrush(Color.FromRgb(127, 140, 141));
        }

        /// <summary>
        /// 根据状态字符串获取对应的纯色画刷
        /// </summary>
        /// <param name="status">状态字符串（运行中/已停止/错误）</param>
        /// <returns>对应状态的SolidColorBrush</returns>
        private SolidColorBrush GetStatusColorBrush(string status)
        {
            switch (status)
            {
                case "运行中":
                    return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // 绿色
                case "已停止":
                    return new SolidColorBrush(Color.FromRgb(155, 89, 182)); // 紫色
                case "错误":
                    return new SolidColorBrush(Color.FromRgb(231, 76, 60));  // 红色
                default:
                    return new SolidColorBrush(Color.FromRgb(127, 140, 141)); // 浅灰色
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 反向转换未实现（无需使用）
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 懒加载创建单例实例
            return _instance ?? (_instance = new StatusToColorConverter());
        }
    }

    /// <summary>
    /// 布尔值转颜色转换器（true=绿色，false=红色）
    /// 继承MarkupExtension，支持XAML内联直接使用
    /// </summary>
    public class BoolToColorConverter : MarkupExtension, IValueConverter
    {
        // 单例实例，提升XAML使用时的性能
        private static BoolToColorConverter _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 校验值为布尔类型，返回对应颜色画刷
            if (value is bool isSuccess && isSuccess)
            {
                return Brushes.Green;
            }

            return Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 反向转换未实现（无需使用）
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 懒加载创建单例实例
            return _instance ?? (_instance = new BoolToColorConverter());
        }
    }
    #endregion

    #region 多值转换器（IMultiValueConverter）
    /// <summary>
    /// 成功率转换器（基于总数量和成功数量计算成功率，格式化为保留2位小数的百分比）
    /// 继承MarkupExtension，支持XAML内联直接使用
    /// </summary>
    public class SuccessRateConverter : MarkupExtension, IMultiValueConverter
    {
        // 单例实例，提升XAML使用时的性能
        private static SuccessRateConverter _instance;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 校验值数组长度、类型有效性
            if (values.Length < 2 || !(values[0] is int total) || !(values[1] is int success))
            {
                return "0.00%";
            }

            // 避免除零异常，总数量小于等于0时返回默认百分比
            if (total <= 0)
            {
                return "0.00%";
            }

            // 计算成功率并格式化为保留2位小数的百分比字符串
            double rate = (double)success / total * 100;
            return $"{rate:F2}%";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // 反向转换未实现（无需使用）
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 懒加载创建单例实例
            return _instance ?? (_instance = new SuccessRateConverter());
        }
    }
    #endregion
}