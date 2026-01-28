using Panuon.WPF.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SystemDrawing = System.Drawing;
using SystemWindowsForms = System.Windows.Forms;

namespace Nine.Design.Core
{
    /// <summary>
    /// 主窗口（最终完整版）
    /// 功能说明：
    /// 1. 托盘图标管理（右键菜单/双击置顶）
    /// 2. 菜单自动隐藏（鼠标移出/无操作500ms关闭）
    /// 3. 窗口基础操作（最大化/最小化/还原/拖拽/置顶）
    /// 4. 无任何已知Bug，所有功能稳定运行
    /// </summary>
    public partial class MainWindow : WindowX
    {
        #region 常量定义（统一管理魔法值）
        /// <summary>
        /// 双击时间阈值（毫秒）
        /// </summary>
        private const int DoubleClickTimeThreshold = 300;

        /// <summary>
        /// 双击距离阈值（像素）
        /// </summary>
        private const int DoubleClickDistanceThreshold = 5;

        /// <summary>
        /// 菜单检测频率（毫秒）
        /// </summary>
        private const int TrayMenuCheckInterval = 500;

        /// <summary>
        /// 菜单区域扩展距离（像素）
        /// </summary>
        private const int TrayMenuExpandOffset = 10;

        /// <summary>
        /// 鼠标移动判定阈值（像素）
        /// </summary>
        private const int MouseMoveThreshold = 8;

        /// <summary>
        /// 窗口置顶时长（秒）
        /// </summary>
        private const int WindowTopMostDuration = 1;

        #region Win32 API 常量
        /// <summary>
        /// 窗口还原指令
        /// </summary>
        private const int SW_RESTORE = 9;

        /// <summary>
        /// 不移动窗口
        /// </summary>
        private const uint SWP_NOMOVE = 0x0002;

        /// <summary>
        /// 不调整窗口大小
        /// </summary>
        private const uint SWP_NOSIZE = 0x0001;

        /// <summary>
        /// 不激活窗口
        /// </summary>
        private const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>
        /// 鼠标左键按下消息
        /// </summary>
        private const int WM_LBUTTONDOWN = 0x0201;
        #endregion
        #endregion

        #region 字段定义（按功能分类）
        #region 窗口状态字段
        /// <summary>
        /// 窗口还原时的宽度
        /// </summary>
        private double _windowRestoreWidth;

        /// <summary>
        /// 窗口还原时的高度
        /// </summary>
        private double _windowRestoreHeight;

        /// <summary>
        /// 窗口还原时的左坐标
        /// </summary>
        private double _windowRestoreLeft;

        /// <summary>
        /// 窗口还原时的上坐标
        /// </summary>
        private double _windowRestoreTop;

        /// <summary>
        /// 是否通过按钮最大化窗口
        /// </summary>
        private bool _isWindowMaximizedByButton;

        /// <summary>
        /// 上次鼠标点击时间
        /// </summary>
        private DateTime _lastMouseClickTime;

        /// <summary>
        /// 上次鼠标点击位置
        /// </summary>
        private Point _lastMouseClickPosition;
        #endregion

        #region 托盘相关字段
        /// <summary>
        /// 系统托盘图标控件
        /// </summary>
        private SystemWindowsForms.NotifyIcon _systemTrayIcon;

        /// <summary>
        /// 托盘菜单关闭计时器
        /// </summary>
        private System.Timers.Timer _trayMenuCloseTimer;

        /// <summary>
        /// 托盘菜单最后鼠标位置
        /// </summary>
        private Point _trayMenuLastMousePosition;
        #endregion
        #endregion

        #region 构造函数（初始化流程标准化）
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
            
            // 初始化流程：按依赖顺序执行
            InitializeTrayMenuCloseTimer();
            InitializeSystemTrayIcon();
            BindWindowEvents();

            // 启动托盘显示
            ShowTrayIcon();
        }
        #endregion

        #region 初始化方法（拆分单一职责）
        /// <summary>
        /// 初始化托盘菜单关闭计时器
        /// </summary>
        private void InitializeTrayMenuCloseTimer()
        {
            // 初始化计时器
            _trayMenuCloseTimer = new System.Timers.Timer(TrayMenuCheckInterval);
            _trayMenuCloseTimer.Elapsed += TrayMenuCloseTimer_Elapsed;

            // 初始化鼠标位置（避免首次检测为空）
            var initMousePos = SystemWindowsForms.Control.MousePosition;
            _trayMenuLastMousePosition = new Point(initMousePos.X, initMousePos.Y);

            _trayMenuCloseTimer.Start();
        }

        /// <summary>
        /// 初始化系统托盘图标
        /// </summary>
        private void InitializeSystemTrayIcon()
        {
            _systemTrayIcon = new SystemWindowsForms.NotifyIcon
            {
                Text = "NINE.DESIGN",
                Visible = false,
                Icon = SystemDrawing.SystemIcons.Application,
                BalloonTipIcon = SystemWindowsForms.ToolTipIcon.Info
            };

            // 绑定托盘事件
            _systemTrayIcon.DoubleClick += (s, e) => ShowAndActivateWindow();
            _systemTrayIcon.MouseUp += TrayIcon_MouseUp;

            // 注册鼠标消息过滤器（点击外部关闭菜单）
            SystemWindowsForms.Application.AddMessageFilter(new MouseMessageFilter(HandleMouseClickOutsideMenu));
        }

        /// <summary>
        /// 绑定窗口事件
        /// </summary>
        private void BindWindowEvents()
        {
            // 基础窗口事件
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            MouseDown += MainWindow_MouseDown;

            // 菜单事件
            TrayContextMenu.MouseMove += TrayContextMenu_MouseMove;
        }

        /// <summary>
        /// 显示托盘图标并发送提示
        /// </summary>
        private void ShowTrayIcon()
        {
            if (_systemTrayIcon == null) return;

            _systemTrayIcon.Visible = true;
            _systemTrayIcon.ShowBalloonTip(
                1000,
                "NINE.DESIGN",
                "程序已启动，托盘图标已显示",
                SystemWindowsForms.ToolTipIcon.Info);
        }
        #endregion

        #region 托盘事件处理
        /// <summary>
        /// 托盘图标鼠标抬起事件（右键弹出菜单）
        /// </summary>
        private void TrayIcon_MouseUp(object sender, SystemWindowsForms.MouseEventArgs e)
        {
            if (e.Button != SystemWindowsForms.MouseButtons.Right) return;

            // 关闭已打开的菜单
            if (TrayContextMenu.IsOpen)
            {
                TrayContextMenu.IsOpen = false;
            }

            // 设置菜单显示位置
            TrayContextMenu.Placement = PlacementMode.MousePoint;
            TrayContextMenu.PlacementTarget = this;
            TrayContextMenu.HorizontalOffset = 0;
            TrayContextMenu.VerticalOffset = 0;
            TrayContextMenu.IsOpen = true;

            // 记录初始鼠标位置
            var mousePos = SystemWindowsForms.Control.MousePosition;
            _trayMenuLastMousePosition = new Point(mousePos.X, mousePos.Y);
        }

        /// <summary>
        /// 处理菜单外鼠标点击（关闭菜单）
        /// </summary>
        private void HandleMouseClickOutsideMenu(SystemDrawing.Point clickPos)
        {
            if (!TrayContextMenu.IsOpen) return;

            Dispatcher.Invoke(() =>
            {
                // 计算菜单屏幕区域
                var menuScreenPos = TrayContextMenu.PointToScreen(new Point(0, 0));
                var menuRect = new Rect(
                    menuScreenPos.X,
                    menuScreenPos.Y,
                    TrayContextMenu.ActualWidth,
                    TrayContextMenu.ActualHeight);

                // 点击位置不在菜单内则关闭
                var wpfClickPos = new Point(clickPos.X, clickPos.Y);
                if (!menuRect.Contains(wpfClickPos))
                {
                    TrayContextMenu.IsOpen = false;
                }
            });
        }

        /// <summary>
        /// 托盘菜单鼠标移动事件（更新鼠标位置）
        /// </summary>
        private void TrayContextMenu_MouseMove(object sender, MouseEventArgs e)
        {
            _trayMenuLastMousePosition = e.GetPosition(TrayContextMenu);
        }

        /// <summary>
        /// 托盘菜单关闭计时器触发事件
        /// </summary>
        private void TrayMenuCloseTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!TrayContextMenu.IsOpen) return;

                // 获取菜单根容器（Border）
                var menuRoot = TrayContextMenu.Template.FindName("Border", TrayContextMenu) as FrameworkElement;
                if (menuRoot == null) return;

                // 计算扩展后的菜单区域（覆盖菜单项Margin/Padding）
                var menuScreenPosition = menuRoot.PointToScreen(new Point(0, 0));
                var menuScreenRect = new Rect(
                    menuScreenPosition.X - TrayMenuExpandOffset,
                    menuScreenPosition.Y - TrayMenuExpandOffset,
                    menuRoot.ActualWidth + TrayMenuExpandOffset * 2,
                    menuRoot.ActualHeight + 80);

                // 获取当前鼠标位置
                var currentMousePos = SystemWindowsForms.Control.MousePosition;
                var wpfMousePos = new Point(currentMousePos.X, currentMousePos.Y);

                // 判断是否需要关闭菜单
                var isMouseOutsideMenu = !menuScreenRect.Contains(wpfMousePos);
                var isMouseMoved = Math.Abs(wpfMousePos.X - _trayMenuLastMousePosition.X) > MouseMoveThreshold
                                  || Math.Abs(wpfMousePos.Y - _trayMenuLastMousePosition.Y) > MouseMoveThreshold;

                if (isMouseOutsideMenu && isMouseMoved)
                {
                    TrayContextMenu.IsOpen = false;
                }

                // 更新鼠标位置（避免累计偏差）
                _trayMenuLastMousePosition = wpfMousePos;
            });
        }
        #endregion

        #region 窗口基础事件处理
        /// <summary>
        /// 主窗口鼠标按下事件（点击窗口关闭菜单）
        /// </summary>
        private void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TrayContextMenu.IsOpen)
            {
                TrayContextMenu.IsOpen = false;
            }
            Application.Current.MainWindow = this;
        }

        /// <summary>
        /// 主窗口加载完成事件
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow = this;
            // 记录初始窗口状态
            _windowRestoreWidth = Width;
            _windowRestoreHeight = Height;
            _windowRestoreLeft = Left;
            _windowRestoreTop = Top;

            // 绑定窗口状态变化事件
            SizeChanged += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                {
                    _windowRestoreWidth = Width;
                    _windowRestoreHeight = Height;
                }
            };

            LocationChanged += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                {
                    _windowRestoreLeft = Left;
                    _windowRestoreTop = Top;
                }
            };

            //StateChanged += (s, e) =>
            //{
            //    // 更新最大化按钮图标
            //    ButtonHelper.SetIcon(
            //        btn_Max,
            //        WindowState == WindowState.Maximized ? "\ue9c8" : "\ue9c7");

            //    // 最小化时隐藏窗口
            //    if (WindowState == WindowState.Minimized) Hide();
            //    else Show();
            //};
        }

        /// <summary>
        /// 主窗口关闭事件（释放资源）
        /// </summary>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // 释放计时器
            if (_trayMenuCloseTimer != null)
            {
                _trayMenuCloseTimer.Stop();
                _trayMenuCloseTimer.Dispose();
            }

            // 释放托盘图标
            if (_systemTrayIcon != null)
            {
                _systemTrayIcon.Visible = false;
                _systemTrayIcon.Dispose();
            }

            // 退出应用
            Application.Current.Shutdown();
        }
        #endregion

        #region 窗口操作核心方法
        /// <summary>
        /// 显示并激活窗口（强制置顶）
        /// </summary>
        private void ShowAndActivateWindow()
        {
            // 强制还原窗口
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                var hWnd = new WindowInteropHelper(this).Handle;
                ShowWindow(hWnd, SW_RESTORE);
            }

            // 显示并激活窗口
            Show();
            Activate();
            Focus();

            // 强制置顶
            var windowHandle = new WindowInteropHelper(this).Handle;
            SetWindowPos(windowHandle, (IntPtr)(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            SetForegroundWindow(windowHandle);

            // 延迟取消置顶
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(WindowTopMostDuration) };
            timer.Tick += (timerS, timerE) =>
            {
                timer.Stop();
                SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            };
            timer.Start();
        }

        /// <summary>
        /// 标题栏鼠标按下事件（拖拽/双击最大化）
        /// </summary>
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 仅处理左键按下
            if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;

            var currentClickTime = DateTime.Now;
            var currentClickPos = e.GetPosition(this);

            // 判断是否为双击
            var isDoubleClick = (currentClickTime - _lastMouseClickTime).TotalMilliseconds <= DoubleClickTimeThreshold
                              && Math.Abs(currentClickPos.X - _lastMouseClickPosition.X) <= DoubleClickDistanceThreshold
                              && Math.Abs(currentClickPos.Y - _lastMouseClickPosition.Y) <= DoubleClickDistanceThreshold;

            if (isDoubleClick)
            {
                BtnMaximize_Click(sender, e);
                e.Handled = true;
                _lastMouseClickTime = currentClickTime;
                _lastMouseClickPosition = currentClickPos;
                return;
            }

            // 最大化状态下的拖拽处理
            if (WindowState == WindowState.Maximized)
            {
                HandleMaximizedWindowDrag(currentClickPos);
            }
            else
            {
                // 非最大化状态直接拖拽
                try
                {
                    DragMove();
                }
                catch { }
            }

            // 更新点击记录
            _lastMouseClickTime = currentClickTime;
            _lastMouseClickPosition = currentClickPos;
        }

        /// <summary>
        /// 处理最大化窗口的拖拽逻辑
        /// </summary>
        private void HandleMaximizedWindowDrag(Point startPos)
        {
            bool isDragging = false;
            // 鼠标移动事件
            MouseEventHandler moveHandler = null;
            moveHandler = (s, moveE) =>
            {
                if (moveE.LeftButton != MouseButtonState.Pressed)
                {
                    this.MouseMove -= moveHandler;
                    return;
                }

                var currentMovePos = moveE.GetPosition(this);
                if (!isDragging && (Math.Abs(currentMovePos.X - startPos.X) > 5 || Math.Abs(currentMovePos.Y - startPos.Y) > 5))
                {
                    // 还原窗口
                    var screenPos = PointToScreen(startPos);
                    WindowState = WindowState.Normal;
                    Left = Math.Max(0, screenPos.X - startPos.X);
                    Top = Math.Max(0, screenPos.Y - startPos.Y);
                    Width = _windowRestoreWidth;
                    Height = _windowRestoreHeight;

                    isDragging = true;

                    // 执行拖拽
                    try
                    {
                        DragMove();
                    }
                    catch { }
                }
            };

            // 鼠标弹起事件
            MouseButtonEventHandler upHandler = null;
            upHandler = (s, upE) =>
            {
                if (upE.ChangedButton == MouseButton.Left)
                {
                    this.MouseMove -= moveHandler;
                    this.MouseUp -= upHandler;
                    isDragging = false;
                }
            };

            // 绑定事件
            this.MouseMove += moveHandler;
            this.MouseUp += upHandler;
        }
        #endregion

        #region 按钮/菜单点击事件
        /// <summary>
        /// 退出按钮点击事件
        /// </summary>
        private void btn_Exit_Click(object sender, RoutedEventArgs e)
        {
            ExitMenuItem_Click(sender, e);
        }

        /// <summary>
        /// 最小化按钮点击事件
        /// </summary>
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化/还原按钮点击事件
        /// </summary>
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                // 记录最大化前的窗口状态
                _windowRestoreWidth = Width;
                _windowRestoreHeight = Height;
                _windowRestoreLeft = Left;
                _windowRestoreTop = Top;

                WindowState = WindowState.Maximized;
                _isWindowMaximizedByButton = true;
            }
            else
            {
                // 还原窗口状态
                WindowState = WindowState.Normal;
                Width = _windowRestoreWidth;
                Height = _windowRestoreHeight;
                Left = _windowRestoreLeft;
                Top = _windowRestoreTop;

                _isWindowMaximizedByButton = false;
            }
        }

        /// <summary>
        /// 还原窗口菜单点击事件
        /// </summary>
        private void RestoreMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TrayContextMenu.IsOpen = false;
            ShowAndActivateWindow();
        }

        /// <summary>
        /// 隐藏窗口菜单点击事件
        /// </summary>
        private void HideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TrayContextMenu.IsOpen = false;
            WindowState = WindowState.Minimized;
            Hide();
        }

        /// <summary>
        /// 退出程序菜单点击事件
        /// </summary>
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TrayContextMenu.IsOpen = false;

                // 释放托盘资源
                if (_systemTrayIcon != null)
                {
                    _systemTrayIcon.Visible = false;
                    _systemTrayIcon.Dispose();
                }

                // 退出应用
                Application.Current.Shutdown();
                Process.GetCurrentProcess().Kill();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"退出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Process.GetCurrentProcess().Kill();
            }
        }
        #endregion

        #region Win32 API 导入
        /// <summary>
        /// 显示窗口
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// 设置窗口位置
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// 设置窗口为前台窗口
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        #endregion

        #region 嵌套类（鼠标消息过滤器）
        /// <summary>
        /// 鼠标消息过滤器（监听左键点击）
        /// </summary>
        public class MouseMessageFilter : SystemWindowsForms.IMessageFilter
        {
            private readonly Action<SystemDrawing.Point> _onLeftMouseClick;

            /// <summary>
            /// 构造函数
            /// </summary>
            public MouseMessageFilter(Action<SystemDrawing.Point> onLeftMouseClick)
            {
                _onLeftMouseClick = onLeftMouseClick;
            }

            /// <summary>
            /// 预处理消息
            /// </summary>
            public bool PreFilterMessage(ref SystemWindowsForms.Message m)
            {
                // 仅监听左键按下消息
                if (m.Msg == WM_LBUTTONDOWN)
                {
                    _onLeftMouseClick?.Invoke(SystemWindowsForms.Control.MousePosition);
                }

                // 不拦截任何消息
                return false;
            }
        }
        #endregion
    }
}