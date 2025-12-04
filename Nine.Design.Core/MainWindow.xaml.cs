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

        // 双击判断变量
        private DateTime _lastClickTime; // 上次点击时间
        private Point _lastClickPos;     // 上次点击位置
        private const int _doubleClickTime = 300; // 双击时间阈值（毫秒）
        private const int _doubleClickDist = 5;   // 双击位置偏差阈值（像素）

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();

            Loaded += (s, e) =>
            {
                // 初始记录启动尺寸和位置
                _restoreWidth = Width;
                _restoreHeight = Height;
                _restoreLeft = Left;
                _restoreTop = Top;

                // 监听尺寸变化（正常状态下更新）
                SizeChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Normal)
                    {
                        _restoreWidth = ActualWidth;
                        _restoreHeight = ActualHeight;
                    }
                };

                // 监听位置变化（正常状态下更新）
                LocationChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Normal)
                    {
                        _restoreLeft = Left;
                        _restoreTop = Top;
                    }
                };

                // 监听窗口状态变化（最大化/还原时同步图标）
                StateChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Maximized)
                    {
                        ButtonHelper.SetIcon(btn_Max, "\ue9c8"); // 还原图标
                    }
                    else
                    {
                        ButtonHelper.SetIcon(btn_Max, "\ue9c7"); // 最大化图标
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

        #region 最大化/还原功能（优化逻辑）
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                // 记录当前状态后最大化
                _restoreWidth = ActualWidth;
                _restoreHeight = ActualHeight;
                _restoreLeft = Left;
                _restoreTop = Top;
                _isMaximizedByButton = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                // 还原到之前记录的状态
                WindowState = WindowState.Normal;
                Width = _restoreWidth;
                Height = _restoreHeight;
                Left = _restoreLeft;
                Top = _restoreTop;
                _isMaximizedByButton = false;
            }
        }
        #endregion

        #region 标题栏拖动+双击逻辑
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 仅处理左键
            if (e.ChangedButton != MouseButton.Left)
                return;

            var currentTime = DateTime.Now;
            var currentPos = e.GetPosition(this);

            // 判断是否为双击
            bool isDoubleClick = (currentTime - _lastClickTime).TotalMilliseconds <= _doubleClickTime
                              && Math.Abs(currentPos.X - _lastClickPos.X) <= _doubleClickDist
                              && Math.Abs(currentPos.Y - _lastClickPos.Y) <= _doubleClickDist;

            if (isDoubleClick)
            {
                // 双击：切换最大化/还原（无论当前状态）
                BtnMaximize_Click(sender, e);
                e.Handled = true;
                return;
            }

            // 更新上次点击记录
            _lastClickTime = currentTime;
            _lastClickPos = currentPos;

            // 拖动逻辑：最大化状态下先还原再拖动
            if (WindowState == WindowState.Maximized)
            {
                // 计算还原后的位置（基于鼠标点击位置）
                var screenPos = PointToScreen(currentPos);
                WindowState = WindowState.Normal;

                // 确保窗口还原后不超出屏幕
                Left = Math.Max(0, screenPos.X - currentPos.X * (ActualWidth / RestoreBounds.Width));
                Top = Math.Max(0, screenPos.Y - currentPos.Y * (ActualHeight / RestoreBounds.Height));
                Width = _restoreWidth;
                Height = _restoreHeight;

                // 还原后执行拖动
                DragMove();
            }
            else
            {
                // 正常状态直接拖动
                DragMove();
            }
        }
        #endregion
    }
}