using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nine.Design.Core.Helpers
{
    /// <summary>
    /// 加载动画控制附加属性（修复嵌套控件查找问题）
    /// </summary>
    public static class LoadingAnimationHelper
    {
        #region 触发加载动画（打开）
        public static bool GetTriggerShowAnimation(DependencyObject obj) => (bool)obj.GetValue(TriggerShowAnimationProperty);
        public static void SetTriggerShowAnimation(DependencyObject obj, bool value) => obj.SetValue(TriggerShowAnimationProperty, value);

        public static readonly DependencyProperty TriggerShowAnimationProperty =
            DependencyProperty.RegisterAttached(
                "TriggerShowAnimation",
                typeof(bool),
                typeof(LoadingAnimationHelper),
                new PropertyMetadata(false, OnTriggerShowAnimationChanged));

        private static void OnTriggerShowAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Grid loadingGrid) || !(e.NewValue is bool isTrigger) || !isTrigger) return;

            // 1. 显示前先把Grid设为Auto宽度（占满空间）
            loadingGrid.Width = double.NaN;

            // 2. 🔥 修复：递归查找嵌套的Loading_Img控件
            var loadingImg = FindVisualChild<UIElement>(loadingGrid, "Loading_Img");
            // 查找显示动画
            var storyboard = loadingGrid.FindResource("Loading_Start") as Storyboard;

            if (storyboard != null && loadingImg != null)
            {
                storyboard.Begin();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("警告：Loading_Start动画或Loading_Img控件未找到");
                // 降级处理：直接显示
                loadingGrid.Opacity = 0.8;
                loadingImg.Opacity = 1;
            }

            // 触发后自动重置
            SetTriggerShowAnimation(loadingGrid, false);
        }
        #endregion

        #region 触发关闭动画（隐藏）
        public static bool GetTriggerHideAnimation(DependencyObject obj) => (bool)obj.GetValue(TriggerHideAnimationProperty);
        public static void SetTriggerHideAnimation(DependencyObject obj, bool value) => obj.SetValue(TriggerHideAnimationProperty, value);

        public static readonly DependencyProperty TriggerHideAnimationProperty =
            DependencyProperty.RegisterAttached(
                "TriggerHideAnimation",
                typeof(bool),
                typeof(LoadingAnimationHelper),
                new PropertyMetadata(false, OnTriggerHideAnimationChanged));

        private static void OnTriggerHideAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Grid loadingGrid) || !(e.NewValue is bool isTrigger) || !isTrigger) return;

            // 1. 查找关闭动画
            var storyboard = loadingGrid.FindResource("Loading_Hide") as Storyboard;
            if (storyboard != null)
            {
                // 动画完成后重置Grid状态
                storyboard.Completed += (s, args) =>
                {
                    loadingGrid.Width = 0; // 0宽度，不占空间
                    loadingGrid.Opacity = 0; // 完全透明
                };
                storyboard.Begin();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("警告：Loading_Hide动画未找到，直接隐藏");
                // 降级处理：直接隐藏
                loadingGrid.Width = 0;
                loadingGrid.Opacity = 0;
                // 🔥 同时隐藏Loading_Img
                var loadingImg = FindVisualChild<UIElement>(loadingGrid, "Loading_Img");
                loadingImg.Opacity = 0;
            }

            // 触发后自动重置
            SetTriggerHideAnimation(loadingGrid, false);
        }
        #endregion

        #region 🔥 新增：递归查找可视化树中的子控件（核心修复）
        /// <summary>
        /// 递归查找可视化树中的指定名称控件
        /// </summary>
        /// <typeparam name="T">控件类型</typeparam>
        /// <param name="parent">父控件</param>
        /// <param name="childName">控件名称</param>
        /// <returns>找到的控件，未找到返回null</returns>
        private static T FindVisualChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (child as FrameworkElement)?.Name == childName)
                {
                    foundChild = typedChild;
                    break;
                }
                else
                {
                    // 递归查找子控件的子控件
                    foundChild = FindVisualChild<T>(child, childName);
                    if (foundChild != null) break;
                }
            }
            return foundChild;
        }
        #endregion
    }
}