using Panuon.WPF.UI;
using System.Windows;

namespace Nine.Design.Core.Helpers
{
    /// <summary>
    /// Panuon Toast 通用通知工具类
    /// 封装Toast调用逻辑，自动处理WindowX校验、异常兜底，支持自定义图标/位置/时长/偏移量
    /// </summary>
    public static class ToastHelper
    {
        /// <summary>
        /// 显示通用Toast通知（核心方法）
        /// </summary>
        /// <param name="message">通知内容</param>
        /// <param name="icon">通知图标（Info/Success/Warning/Error）</param>
        /// <param name="position">通知显示位置（默认：顶部）</param>
        /// <param name="durationMs">显示时长（毫秒，默认：500ms）</param>
        /// <param name="offset">偏移量（像素，控制离窗口边缘的距离，默认：60px）</param>
        public static void ShowToast(
            string message,
            MessageBoxIcon icon,
            ToastPosition position = ToastPosition.Top,
            int durationMs = 500,
            int offset = 60)
        {
            // 参数校验：避免无效值
            if (string.IsNullOrWhiteSpace(message))
            {
                System.Diagnostics.Debug.WriteLine("Toast通知内容不能为空");
                return;
            }
            durationMs = Math.Max(100, durationMs); // 最小显示100ms，避免传0或负数
            offset = Math.Max(0, offset); // 偏移量不能为负

            try
            {
                // 1. 兜底保障：确保有有效的WindowX窗口（解决调试时MainWindow为null）
                EnsureValidWindowX();

                // 2. 调用Panuon Toast核心方法
                Toast.Show(
                    message: message,
                    icon: icon,
                    position: position,
                    offset: offset,
                    durationMs: durationMs,
                    targetWindow: ToastWindow.ActiveWindow
                );
            }
            catch (InvalidOperationException ex)
            {
                // 兼容场景：WindowX窗口不存在/未加载
                System.Diagnostics.Debug.WriteLine($"【Toast兼容模式】显示失败：{ex.Message}");
                //MessageBox.Show(message, "提示", MessageBoxButton.OK, icon);
            }
            catch (Exception ex)
            {
                // 全局异常捕获：避免UI线程崩溃
                System.Diagnostics.Debug.WriteLine($"【Toast异常】{ex.GetType().Name}：{ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"操作成功，但通知显示失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #region 重载方法（简化调用）
        /// <summary>
        /// 显示默认样式Toast（信息图标+顶部+3秒+60px偏移）
        /// </summary>
        /// <param name="message">通知内容</param>
        public static void ShowToast(string message)
        {
            ShowToast(message, MessageBoxIcon.Info, ToastPosition.Top, 3000, 60);
        }

        /// <summary>
        /// 显示指定图标的Toast（默认：顶部+3秒+60px偏移）
        /// </summary>
        /// <param name="message">通知内容</param>
        /// <param name="icon">通知图标</param>
        public static void ShowToast(string message, MessageBoxIcon icon)
        {
            ShowToast(message, icon, ToastPosition.Top, 3000, 60);
        }
        #endregion

        #region 私有辅助方法
        /// <summary>
        /// 兜底保障：确保有有效的WindowX类型窗口
        /// 解决调试时MainWindow为null/类型不匹配的问题
        /// </summary>
        private static void EnsureValidWindowX()
        {
            // 场景1：MainWindow为null → 遍历查找已激活的WindowX
            if (Application.Current.MainWindow == null)
            {
                var activeWindowX = Application.Current.Windows
                    .OfType<WindowX>()
                    .FirstOrDefault(w => w.IsLoaded && w.IsActive);

                // 找到有效WindowX则设为MainWindow，供Toast使用
                if (activeWindowX != null)
                {
                    Application.Current.MainWindow = activeWindowX;
                    System.Diagnostics.Debug.WriteLine($"【Toast辅助】已自动绑定有效WindowX：{activeWindowX.Title}");
                }
            }
            // 场景2：MainWindow不是WindowX → 输出调试警告
            else if (!(Application.Current.MainWindow is WindowX))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"【Toast警告】MainWindow类型为{Application.Current.MainWindow.GetType().Name}，非WindowX类型，可能导致Toast显示异常");
            }
        }
        #endregion
    }
}