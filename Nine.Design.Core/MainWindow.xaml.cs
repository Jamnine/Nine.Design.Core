using Panuon.WPF.UI;
using System;
using System.Windows;
using System.Windows.Input;
// 新增托盘相关引用
using System.Windows.Forms;
using System.Drawing;
using System.Windows.Interop;

namespace Nine.Design.Core
{
    public partial class MainWindow : WindowX
    {
        // 记录还原信息（修复 ActualWidth/ActualHeight 错误，改用 Window 原生属性）
        private double _restoreWidth;
        private double _restoreHeight;
        private double _restoreLeft;
        private double _restoreTop;
        private bool _isMaximizedByButton = false;

        // 双击判断变量
        private DateTime _lastClickTime;
        private System.Windows.Point _lastClickPos;
        private const int _doubleClickTime = 300;
        private const int _doubleClickDist = 5;

        // 系统托盘控件（核心）
        private NotifyIcon _trayIcon;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();

            // 初始化系统托盘（启动时隐藏，仅最小化时显示）
            InitSystemTray();

            Loaded += (s, e) =>
            {
                // 初始记录启动尺寸和位置（Window 用 Width/Left，而非 ActualWidth/ActualLeft）
                _restoreWidth = Width;
                _restoreHeight = Height;
                _restoreLeft = Left;
                _restoreTop = Top;

                // 监听尺寸变化（正常状态下更新）
                SizeChanged += (sender, args) =>
                {
                    if (WindowState == WindowState.Normal)
                    {
                        _restoreWidth = Width;
                        _restoreHeight = Height;
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

                // 监听窗口状态变化：仅最小化时进入托盘，其他状态隐藏托盘
                StateChanged += (sender, args) =>
                {
                    // 同步最大化/还原图标
                    ButtonHelper.SetIcon(btn_Max, WindowState == WindowState.Maximized ? "\ue9c8" : "\ue9c7");

                    // 核心逻辑：最小化 → 隐藏窗口 + 显示托盘；非最小化 → 显示窗口 + 隐藏托盘
                    if (WindowState == WindowState.Minimized)
                    {
                        this.Hide(); // 隐藏主窗口（任务栏也会消失）
                        _trayIcon.Visible = true; // 显示托盘图标
                        // 可选：最小化时显示气泡提示
                        _trayIcon.ShowBalloonTip(1000, "NINE.DESIGN", "程序已最小化到系统托盘", ToolTipIcon.Info);
                    }
                    else
                    {
                        _trayIcon.Visible = false; // 非最小化时隐藏托盘
                    }
                };
            };

            // 窗口关闭时强制清理托盘，避免进程残留
            Closed += (s, e) =>
            {
                _trayIcon?.Dispose(); // 释放托盘资源
                System.Windows.Application.Current.Shutdown();
            };
        }

        #region 系统托盘初始化（核心：启动时隐藏）
        private void InitSystemTray()
        {
            // 1. 创建托盘控件
            _trayIcon = new NotifyIcon
            {
                Text = "NINE.DESIGN", // 鼠标悬浮托盘时显示的文本
                Visible = false, // 启动时不显示（关键！）
                // 设置托盘图标（替换为你的图标路径，或用系统默认图标）
                // 方式1：自定义图标（推荐）
                // Icon = new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources/app.ico")),
                // 方式2：系统默认图标（无需文件）
                Icon = SystemIcons.Application
            };

            // 2. 托盘图标双击 → 还原窗口
            _trayIcon.DoubleClick += (s, e) =>
            {
                RestoreWindow();
            };

            // 3. 托盘右键菜单（还原/退出）
            var contextMenu = new ContextMenuStrip();
            // 还原窗口菜单
            contextMenu.Items.Add("还原窗口", null, (s, e) => RestoreWindow());
            // 退出程序菜单
            contextMenu.Items.Add("退出程序", null, (s, e) => ExitApplication());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// 还原窗口到正常状态
        /// </summary>
        private void RestoreWindow()
        {
            this.Show(); // 显示主窗口
            this.WindowState = WindowState.Normal; // 还原为正常状态
            this.Activate(); // 激活窗口（置顶）
            // 恢复窗口位置和尺寸（无偏差）
            this.Width = _restoreWidth;
            this.Height = _restoreHeight;
            this.Left = _restoreLeft;
            this.Top = _restoreTop;
        }

        /// <summary>
        /// 退出程序（清理托盘+关闭窗口）
        /// </summary>
        private void ExitApplication()
        {
            _trayIcon?.Dispose(); // 必须释放托盘，否则进程残留
            this.Close();
            System.Windows.Application.Current.Shutdown();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
        #endregion

        #region 关闭程序（保留原逻辑，集成托盘清理）
        private void btn_Exit_Click(object sender, RoutedEventArgs e)
        {
            ReleaseCustomResources();
            ExitApplication(); // 改用新的退出逻辑，清理托盘
        }

        private void ReleaseCustomResources()
        {
            // 你的资源释放逻辑（如数据库连接、文件流等）
        }
        #endregion

        #region 最小化功能（点击按钮 → 进入托盘）
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized; // 触发 StateChanged → 进入托盘
        }
        #endregion

        #region 最大化/还原功能（无偏差）
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                // 最大化前记录当前状态（无偏差）
                _restoreWidth = Width;
                _restoreHeight = Height;
                _restoreLeft = Left;
                _restoreTop = Top;
                _isMaximizedByButton = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                // 还原时直接复用记录值，无计算偏差
                WindowState = WindowState.Normal;
                Width = _restoreWidth;
                Height = _restoreHeight;
                Left = _restoreLeft;
                Top = _restoreTop;
                _isMaximizedByButton = false;
            }
        }
        #endregion

        #region 标题栏拖动+双击逻辑（修复位置偏差）
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            var currentTime = DateTime.Now;
            var currentPos = e.GetPosition(this);

            // 判断双击
            bool isDoubleClick = (currentTime - _lastClickTime).TotalMilliseconds <= _doubleClickTime
                              && Math.Abs(currentPos.X - _lastClickPos.X) <= _doubleClickDist
                              && Math.Abs(currentPos.Y - _lastClickPos.Y) <= _doubleClickDist;

            if (isDoubleClick)
            {
                BtnMaximize_Click(sender, e);
                e.Handled = true;
                return;
            }

            // 更新点击记录
            _lastClickTime = currentTime;
            _lastClickPos = currentPos;

            // 拖动逻辑：最大化状态下还原（无偏差）
            if (WindowState == WindowState.Maximized)
            {
                var screenPos = PointToScreen(currentPos);
                WindowState = WindowState.Normal;

                // 简化位置计算，避免比例偏差
                Left = Math.Max(0, screenPos.X - currentPos.X);
                Top = Math.Max(0, screenPos.Y - currentPos.Y);
                Width = _restoreWidth;
                Height = _restoreHeight;

                DragMove();
            }
            else
            {
                DragMove();
            }
        }
        #endregion
    }
}