using Panuon.WPF.UI;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nine.Design.Core
{
    public partial class MainWindow : WindowX
    {
        // 记录还原信息（实时更新）
        private double _restoreWidth;   // 实时更新的宽度
        private double _restoreHeight;  // 实时更新的高度
        private double _restoreLeft;    // 实时更新的X坐标
        private double _restoreTop;     // 实时更新的Y坐标
        private bool _isMaximizedByButton = false; // 标记是否通过按钮最大化

        public MainWindow()
        {
            InitializeComponent();

            // 初始化时绑定尺寸和位置变化事件（实时记录）
            Loaded += (s, e) =>
            {
                // 初始记录启动尺寸
                _restoreWidth = Width;
                _restoreHeight = Height;
                _restoreLeft = Left;
                _restoreTop = Top;

                // 监听尺寸变化（手动拖动调整大小时更新）
                SizeChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Normal) // 仅在正常状态下更新
                    {
                        _restoreWidth = ActualWidth;
                        _restoreHeight = ActualHeight;
                    }
                };

                // 监听位置变化（手动拖动移动位置时更新）
                LocationChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Normal) // 仅在正常状态下更新
                    {
                        _restoreLeft = Left;
                        _restoreTop = Top;
                    }
                };
            };
        }

        #region 关闭程序（保留原逻辑）
        private void btn_Exit_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCustomResources();
            Close();
            Application.Current.Shutdown();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        private void ReleaseCustomResources()
        {
            // 你的资源释放逻辑
        }
        #endregion

        #region 最小化功能
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        #endregion

        #region 最大化/还原功能（适配手动拖动最大化）
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                // 通过按钮最大化时，记录当前状态并标记
                _isMaximizedByButton = true;
                WindowState = WindowState.Maximized;
                ButtonHelper.SetIcon(btn_Max, "\ue9c8"); // 切换还原图标
            }
            else
            {
                // 还原时，无论之前是按钮还是手动最大化，都用最新记录的尺寸
                WindowState = WindowState.Normal;
                Width = _restoreWidth;
                Height = _restoreHeight;
                Left = _restoreLeft;
                Top = _restoreTop;
                _isMaximizedByButton = false;
                ButtonHelper.SetIcon(btn_Max, "\ue9c7"); // 切换最大化图标
            }
        }
        #endregion

        // 记录双击判断的关键变量
        private DateTime _lastClickTime; // 上次点击时间
        private Point _lastClickPos;     // 上次点击位置
        private const int _doubleClickTime = 300; // 双击时间阈值（毫秒）
        private const int _doubleClickDist = 5;   // 双击位置偏差阈值（像素）

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 仅处理左键
            if (e.ChangedButton != MouseButton.Left)
                return;

            // 获取当前点击信息
            var currentTime = DateTime.Now;
            var currentPos = e.GetPosition(this);

            // 判断是否为双击：时间间隔<300ms 且 位置偏差<5像素
            bool isDoubleClick = (currentTime - _lastClickTime).TotalMilliseconds <= _doubleClickTime
                              && Math.Abs(currentPos.X - _lastClickPos.X) <= _doubleClickDist
                              && Math.Abs(currentPos.Y - _lastClickPos.Y) <= _doubleClickDist;

            if (isDoubleClick)
            {
                // 双击：切换最大化/还原
                BtnMaximize_Click(sender, e);
                e.Handled = true; // 阻止触发拖动
                return;
            }

            // 不是双击：更新上次点击记录，并执行拖动逻辑
            _lastClickTime = currentTime;
            _lastClickPos = currentPos;

            // 拖动逻辑（保留原功能）
            if (WindowState == WindowState.Maximized)
            {
                var screenMousePos = PointToScreen(currentPos);
                var newLeft = Math.Max(0, screenMousePos.X - currentPos.X);
                var newTop = Math.Max(0, screenMousePos.Y - currentPos.Y);

                WindowState = WindowState.Normal;
                Width = _restoreWidth;
                Height = _restoreHeight;
                Left = newLeft;
                Top = newTop;
                _isMaximizedByButton = false;
                ButtonHelper.SetIcon(btn_Max, "&#xe9c7;");
            }
            DragMove();
        }
    }
}