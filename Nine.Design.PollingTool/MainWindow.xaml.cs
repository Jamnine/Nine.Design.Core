using Newtonsoft.Json;
using Panuon.WPF.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static HardwareMonitorHelper;

namespace Nine.Design.PollingTool
{
    /// <summary>
    /// 主窗口（重构后：单日志面板 + 机器过滤按钮）
    /// </summary>
    public partial class MainWindow : WindowX, INotifyPropertyChanged
    {
        // 基础配置
        private Dictionary<int, MachineDataModel> _allMachineData = new Dictionary<int, MachineDataModel>();
        private MachineDataModel _currentDisplayData = new MachineDataModel(); // 当前显示的统计数据（全部/指定机器）
        private HttpMethod _selectedHttpMethod = HttpMethod.Post;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _hardwarePollingCts; // 硬件轮询专属Cts（新增）
        private bool _isHardwarePolling = false; // 硬件轮询标记（新增）
        private List<Task> _pollingTasks = new List<Task>();
        private HttpClient _httpClient;
        private bool _isPolling = false;
        private SynchronizationContext _uiContext;
        // 新增：当前选中的测试模式（默认高频压测）
        private TestMode _currentTestMode = TestMode.HighFrequency;

        // 日志配置
        private readonly List<Brush> _logBrushes = new List<Brush>
        {
            Brushes.Black, Brushes.DarkBlue, Brushes.DarkGreen, Brushes.DarkRed,
            Brushes.DarkOrange, Brushes.Purple, Brushes.Teal, Brushes.Maroon
        };
        private FixedLogType _currentSelectedFixedLogType = FixedLogType.All;
        private int _requestTimeoutSeconds = 10;
        private string _tempLogFilePath;
        private string _logDirectory;
        private readonly object _logLock = new object();
        private bool _isLogInitialized = false;
        private const int FileOperationRetryCount = 3;
        private const int FileOperationRetryDelay = 100;

        // 核心修改：将「每秒请求数」改为「请求间隔秒数」
        private int _requestConfigValue = 2; // 配置值（随模式变化：高频=每秒次数，稳定=间隔秒数）
        private int _requestIntervalSeconds = 2;// 最终用于请求控制的间隔毫秒数（内部计算使用）
        private readonly ConcurrentQueue<LogEntry> _globalLogQueue = new ConcurrentQueue<LogEntry>(); // 全局日志队列（带机器标记）
        private List<LogEntry> _filteredLogList = new List<LogEntry>(); // 过滤后的日志列表
        private int _currentFilterMachineId = 0; // 当前过滤的机器ID（0=全部）
        private DispatcherTimer _logUiTimer;


        // 端口监控与统计
        private int _maxPortUsage = 50000;
        private DispatcherTimer _portMonitorTimer;
        private int _currentPortCount = 0;
        private int _totalRequestsSent = 0;
        private int _totalSuccessfulRequests = 0;
        private readonly object _requestCountLock = new object();
        private DateTime _pollingStartTime;
        private DispatcherTimer _statsUpdateTimer;

        // 多机器统计
        private readonly ConcurrentDictionary<int, int> _machineTotalRequests = new ConcurrentDictionary<int, int>();
        private readonly ConcurrentDictionary<int, int> _machineSuccessRequests = new ConcurrentDictionary<int, int>();
        private readonly ConcurrentDictionary<int, Dictionary<string, int>> _errorStats = new ConcurrentDictionary<int, Dictionary<string, int>>();
        private readonly ConcurrentDictionary<int, Tuple<double, double>> _responseTimeExtremes = new ConcurrentDictionary<int, Tuple<double, double>>();
        private readonly ConcurrentDictionary<int, DateTime> _machineLastRequestTime = new ConcurrentDictionary<int, DateTime>();
        private TimeSpan _totalPollingTime = TimeSpan.Zero;

        private HistoryItem _currentEditingItem = null;

        // 构造函数
        public MainWindow()
        {
            InitializeComponent();
            this.FontSize = 10;
            InitializeTcpParameters();
            RegisterGlobalExceptionHandlers();
            InitializeLogSystem();
            InitializeLogUiTimer();
            InitializeHttpClient();
            InitializePortMonitorTimer();
            InitializeStatsTimer();

            Loaded += (s, e) =>
            {
                _uiContext = SynchronizationContext.Current;
                //rbGet.Checked += HttpMethodChecked;
                //rbPost.Checked += HttpMethodChecked;
                TxtEndpoint_TextChanged(null, null);
                txtEndpoint.TextChanged += TxtEndpoint_TextChanged;
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
                _cts = new CancellationTokenSource();
                UpdateMachineFilterButtons(); // 替换原有UpdateMachineTabItems
                txtMachineCount.ValueChanged += txtMachineCount_ValueChanged;

                // 绑定请求间隔输入框事件（核心修改：对应请求间隔秒数）
                txtPollInterval.ValueChanged += txtPollInterval_ValueChanged;
                // 默认值设置为2秒
                txtPollInterval.Value = _requestIntervalSeconds;

                //if (btnLoadFromHistory != null)
                //{
                //    btnLoadFromHistory.Click += LoadFromHistory_Click;
                //}
                // 初始化配置描述标签
                UpdateConfigDescLabel();
                cboTestMode.SelectionChanged += CboTestMode_SelectionChanged;

                LoadHistory();
                AddLogEntry("[系统] 程序启动，实时统计面板已启用", 0);
                AddLogEntry($"[系统] 默认请求间隔：{_requestIntervalSeconds}秒/次 | 端口上限：{_maxPortUsage}", 0);
                StartHardwarePollingAsync(5000);

            };

            Closing += MainWindow_Closing;
        }
        /// <summary>
        /// 独立的异步方法：启动硬件轮询（允许async void，作为事件后续逻辑）
        /// </summary>
        /// <param name="pollIntervalMs">轮询间隔</param>
        private async void StartHardwarePollingAsync(int pollIntervalMs)
        {
            // 直接调用原有异步轮询方法，此处可await
            await StartHardwarePolling(pollIntervalMs);
        }
        /// <summary>
        /// 测试模式切换事件（核心：切换模式并更新逻辑）
        /// </summary>
        private void CboTestMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cboTestMode.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
                {
                    // 更新当前测试模式
                    _currentTestMode = tag == "HighFrequency" ? TestMode.HighFrequency : TestMode.StableMonitor;

                    // 更新配置描述标签
                    UpdateConfigDescLabel();

                    // 重新加载配置值（实时生效）
                    UpdateRequestConfigFromInput();

                    // 输出日志
                    string modeDesc = _currentTestMode == TestMode.HighFrequency ? "高频压测模式（一秒几次）" : "稳定监控模式（几秒一次）";
                    AddLogEntry($"[系统] 已切换测试模式：{modeDesc}，当前配置值：{_requestConfigValue}", 0);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 切换测试模式失败：{ex.Message}", 0, true);
            }
        }

        /// <summary>
        /// 动态更新配置值描述标签
        /// </summary>
        private void UpdateConfigDescLabel()
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentTestMode == TestMode.HighFrequency)
                {
                    lblConfigDesc.Text = "每秒发起请求次数";
                }
                else
                {
                    lblConfigDesc.Text = "每次请求间隔秒数";
                }
            });
        }

        // 窗口大小改变事件（保持不变）
        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double scale = Math.Max(0.8, Math.Min(1.5, e.NewSize.Width / 1400));
            this.FontSize = 12 * scale;
        }

        #region 初始化方法（保持原有，仅修改日志提示）
        private void InitializeTcpParameters()
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 1000;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.SetTcpKeepAlive(true, 30000, 10000);

                if (Environment.Version.Major >= 3)
                {
                    ServicePointManager.ReusePort = true;
                    AddLogEntry("[系统] TCP端口重用已启用", 0, false, FixedLogType.System);
                }

                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("MaxUserPort", 65534, Microsoft.Win32.RegistryValueKind.DWord);
                            key.SetValue("TcpTimedWaitDelay", 30, Microsoft.Win32.RegistryValueKind.DWord);
                            AddLogEntry("[系统] TCP注册表参数已配置（需重启生效）", 0);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    AddLogEntry("[系统] 警告：未以管理员运行，无法修改TCP注册表参数", 0, true);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] TCP参数初始化失败：{ex.Message}", 0);
            }
        }

        private void InitializeHttpClient()
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = false,
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                _httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds)
                };

                AddLogEntry("[系统] HttpClient初始化完成（连接池大小：1000，已启用SSL证书忽略）", 0, false, FixedLogType.System);
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] HttpClient初始化失败：{ex.Message}", 0, true);
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds)
                };
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            }
        }

        private void InitializePortMonitorTimer()
        {
            _portMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _portMonitorTimer.Tick += PortMonitorTimer_Tick;
            _portMonitorTimer.Start();
            AddLogEntry("[系统] 端口监控定时器已启动（5秒/次）", 0);
        }

        private void InitializeStatsTimer()
        {
            _statsUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsUpdateTimer.Tick += StatsUpdateTimer_Tick;
            _statsUpdateTimer.Start();
            AddLogEntry("[系统] 实时统计定时器已启动（1秒/次）", 0);
        }

        private void InitializeLogSystem()
        {
            try
            {
                _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PollingLogs");
                if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);

                string tempFileName = $"temp_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
                _tempLogFilePath = Path.Combine(_logDirectory, tempFileName);

                using (var fs = new FileStream(_tempLogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.Close();
                }

                _isLogInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"日志系统初始化失败：{ex.Message}\n程序仍可运行，但日志无法持久化",
                    "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void InitializeLogUiTimer()
        {
            // 优化：将间隔从100ms改为50ms，提高日志刷新速度，避免积压
            _logUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _logUiTimer.Tick += LogUiTimer_Tick;
            _logUiTimer.Start();
            AddLogEntry("[系统] 日志UI定时器已启动，间隔：{_logUiTimer.Interval.TotalMilliseconds}ms", 0, false, FixedLogType.System);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            Application.Current.DispatcherUnhandledException += (sender, e) =>
            {
                HandleUnhandledException(e.Exception, "UI线程异常");
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                HandleUnhandledException(ex, "非UI线程异常");
                if (e.IsTerminating) EmergencySaveLog();
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                HandleUnhandledException(e.Exception, "任务调度器异常");
                e.SetObserved();
            };
        }
        #endregion

        #region 定时器回调（保持不变）
        private void PortMonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _currentPortCount = GetCurrentProcessTcpConnectionCount();
                OnPropertyChanged(nameof(CurrentPortCount));
                AddLogEntry($"[监控] 端口占用：{_currentPortCount}/{_maxPortUsage} | 总请求：{_totalRequestsSent} | 成功：{_totalSuccessfulRequests}", 0);

                if (_currentPortCount > _maxPortUsage * 0.8)
                {
                    AddLogEntry($"[警告] 端口占用率已达80%（{_currentPortCount}/{_maxPortUsage}）", 0, true);
                }

                // 更新端口显示
                Dispatcher.Invoke(() =>
                {
                    txtPortUsage.Text = $"{_currentPortCount}/{_maxPortUsage}";
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[监控] 端口统计失败：{ex.Message}", 0);
            }
        }

        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 更新全局统计
                    _totalSuccessfulRequests = _machineSuccessRequests.Values.Sum();
                    _totalRequestsSent = _machineTotalRequests.Values.Sum();

                    // 更新当前显示的统计数据
                    if (_currentFilterMachineId == 0)
                    {
                        // 显示全部数据
                        txtTotalRequests.Text = _totalRequestsSent.ToString();
                        txtSuccessRequests.Text = _totalSuccessfulRequests.ToString();
                        double successRate = _totalRequestsSent > 0 ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100 : 0;
                        txtSuccessRate.Text = $"{successRate:F2}%";
                    }
                    else
                    {
                        // 显示指定机器数据
                        if (_machineTotalRequests.TryGetValue(_currentFilterMachineId, out int machineTotal))
                        {
                            txtTotalRequests.Text = machineTotal.ToString();
                        }
                        else
                        {
                            txtTotalRequests.Text = "0";
                        }

                        if (_machineSuccessRequests.TryGetValue(_currentFilterMachineId, out int machineSuccess))
                        {
                            txtSuccessRequests.Text = machineSuccess.ToString();
                        }
                        else
                        {
                            txtSuccessRequests.Text = "0";
                        }

                        int total = int.Parse(txtTotalRequests.Text);
                        int success = int.Parse(txtSuccessRequests.Text);
                        double successRate = total > 0 ? (double)success / total * 100 : 0;
                        txtSuccessRate.Text = $"{successRate:F2}%";
                    }

                    // 更新运行时长
                    txtRunTime.Text = RunTime;
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[统计] UI更新失败：{ex.Message}", 0);
            }
        }

        private void LogUiTimer_Tick(object sender, EventArgs e)
        {
            UpdateGlobalLogUI(); // 重构：更新全局日志UI（带过滤）
        }

        /// <summary>
        /// 重构：更新全局日志UI，支持机器过滤
        /// </summary>
        /// <summary>
        /// 重构：更新全局日志UI，支持机器过滤（优化：避免重复清空列表，提高性能）
        /// </summary>
        /// <summary>
        /// 重构：更新全局日志UI，支持固定日志类型+机器ID双重过滤（核心修改）
        /// </summary>
        private void UpdateGlobalLogUI()
        {
            if (_globalLogQueue.IsEmpty) return;

            // 1. 出队新日志，加入过滤列表
            List<LogEntry> newLogs = new List<LogEntry>();
            int maxBatchSize = 200; // 每次最多处理200条日志，避免UI卡死
            int count = 0;

            LogEntry logEntry;
            while (_globalLogQueue.TryDequeue(out logEntry) && count < maxBatchSize)
            {
                newLogs.Add(logEntry);
                _filteredLogList.Add(logEntry);
                count++;
            }

            // 2. 过滤日志（核心修改：先过滤固定日志类型，再过滤机器ID）
            List<LogEntry> logsToDisplay = new List<LogEntry>();

            // 第一步：过滤固定日志类型
            if (_currentSelectedFixedLogType == FixedLogType.All)
            {
                // 全部类型：保留所有日志
                logsToDisplay = _filteredLogList.ToList();
            }
            else
            {
                // 指定类型：只保留对应固定类型的日志
                logsToDisplay = _filteredLogList.Where(l => l.LogType == _currentSelectedFixedLogType).ToList();
            }

            // 第二步：过滤机器ID（在固定类型过滤的基础上，再过滤机器）
            if (_currentFilterMachineId != 0)
            {
                logsToDisplay = logsToDisplay.Where(l => l.MachineId == _currentFilterMachineId).ToList();
            }

            // 3. 更新UI（原有逻辑不变，仅使用过滤后的logsToDisplay）
            Dispatcher.Invoke(() =>
            {
                // 限制日志最大数量，防止内存溢出
                if (_filteredLogList.Count > 50000)
                {
                    int removeCount = _filteredLogList.Count - 50000;
                    _filteredLogList.RemoveRange(0, removeCount);
                    // 清空列表，重新添加（超过最大数量时才清空，平时只追加）
                    lbGlobalLogs.Items.Clear();
                }

                // 优化：只添加新日志，不重复添加全部
                if (newLogs.Count > 0 && lbGlobalLogs.Items.Count == 0)
                {
                    // 列表为空时，添加所有过滤后的日志
                    foreach (var log in logsToDisplay)
                    {
                        AddLogItemToUI(log);
                    }
                }
                else if (newLogs.Count > 0)
                {
                    // 列表不为空时，只添加新出队的日志（先过滤新日志，再添加）
                    var newLogsToDisplay = new List<LogEntry>();
                    if (_currentSelectedFixedLogType == FixedLogType.All)
                    {
                        newLogsToDisplay = newLogs;
                    }
                    else
                    {
                        newLogsToDisplay = newLogs.Where(l => l.LogType == _currentSelectedFixedLogType).ToList();
                    }
                    if (_currentFilterMachineId != 0)
                    {
                        newLogsToDisplay = newLogsToDisplay.Where(l => l.MachineId == _currentFilterMachineId).ToList();
                    }

                    foreach (var log in newLogsToDisplay)
                    {
                        AddLogItemToUI(log);
                    }
                }

                // 滚动到最新日志（优化：只有新日志时才滚动，避免频繁滚动）
                if (newLogs.Count > 0 && lbGlobalLogs.Items.Count > 0)
                {
                    lbGlobalLogs.ScrollIntoView(lbGlobalLogs.Items[lbGlobalLogs.Items.Count - 1]);
                }
            });
        }
        /// <summary>
        /// 辅助方法：添加单条日志到UI，避免重复代码
        /// </summary>
        /// <param name="log">日志条目</param>
        private void AddLogItemToUI(LogEntry log)
        {
            ListBoxItem logItem = new ListBoxItem
            {
                Content = log.Message,
                Padding = new Thickness(2)
            };

            if (log.IsError)
            {
                logItem.Foreground = Brushes.Red;
                logItem.FontWeight = FontWeights.Bold;
            }
            else if (log.MachineId > 0)
            {
                int colorIndex = (log.MachineId - 1) % _logBrushes.Count;
                logItem.Foreground = _logBrushes[colorIndex];
            }
            else
            {
                logItem.Foreground = Brushes.DarkGray;
            }

            lbGlobalLogs.Items.Add(logItem);
        }
        #endregion

        #region 核心业务逻辑（核心修改：请求间隔逻辑）
        //private void HttpMethodChecked(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        if (sender is RadioButton radioButton && radioButton.IsChecked.HasValue && radioButton.IsChecked.Value)
        //        {
        //            _selectedHttpMethod = (radioButton == rbGet) ? HttpMethod.Get : HttpMethod.Post;
        //            AddLogEntry($"[系统] 请求方法已切换为: {_selectedHttpMethod.Method}", 0);

        //            Dispatcher.Invoke(() =>
        //            {
        //                rbGet.IsChecked = (radioButton == rbGet);
        //                rbPost.IsChecked = (radioButton == rbPost);
        //            });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AddLogEntry($"[系统] 切换请求方法失败：{ex.Message}", 0, true);
        //    }
        //}

        /// <summary>
        /// 重构：创建/更新机器过滤按钮（替换原有Tab页面创建）
        /// </summary>
        private void UpdateMachineFilterButtons()
        {
            // 确保在UI线程执行（Dispatcher.Invoke 保留，避免跨线程异常）
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. 获取当前机器数量（保留原有逻辑，增加容错）
                    int machineCount = 1;
                    if (txtMachineCount.Value.HasValue && txtMachineCount.Value.Value > 0)
                    {
                        machineCount = (int)txtMachineCount.Value.Value;
                    }

                    // 2. 清空原有机器按钮（关键修正：保留3个固定按钮，只删除动态创建的机器按钮）
                    // 筛选条件：排除3个固定按钮（btnAllLogs、btnHardwareLogs、btnSystemLogs），其余都是动态机器按钮
                    var machineButtons = spMachineFilterButtons.Children
                        .OfType<Button>()
                        .Where(b => b.Name != "btnAllLogs"
                                  && b.Name != "btnHardwareLogs"
                                  && b.Name != "btnSystemLogs")
                        .ToList(); // 先转List，避免遍历过程中集合变更异常

                    foreach (var btn in machineButtons)
                    {
                        // 移除前解绑事件（可选，避免内存泄漏，好习惯）
                        btn.Click -= BtnMachineFilter_Click;
                        spMachineFilterButtons.Children.Remove(btn);
                    }

                    // 3. 清空原有机器数据（保留原有逻辑）
                    _allMachineData.Clear();

                    // 4. 初始化全局统计数据（保留原有逻辑）
                    var totalOverviewData = new MachineDataModel();
                    _allMachineData.Add(0, totalOverviewData);
                    _currentDisplayData = totalOverviewData;

                    // 5. 创建机器按钮和对应数据（保留原有逻辑，优化按钮创建）
                    for (int i = 1; i <= machineCount; i++)
                    {
                        var machineData = new MachineDataModel();
                        _allMachineData.Add(i, machineData);

                        // 创建机器过滤按钮（优化：增加空值判断，避免FindResource失败）
                        Button machineBtn = new Button
                        {
                            Content = $"机器 {i}",
                            // 增加容错：如果样式找不到，不崩溃，使用默认按钮样式
                            Style = FindResource("MachineFilterButtonStyle") as Style ?? new Style(typeof(Button)),
                            Tag = i, // 存储机器ID，用于点击事件区分
                                     // 明确命名（可选，方便调试）
                            Name = $"btnMachine_{i}"
                        };
                        machineBtn.Click += BtnMachineFilter_Click;

                        // 关键：将动态按钮添加到StackPanel中（保留在固定按钮之后，顺序正确）
                        spMachineFilterButtons.Children.Add(machineBtn);
                    }

                    // 6. 打印日志（保留原有逻辑，优化日志格式）
                    AddLogEntry($"[系统] 机器过滤按钮创建成功，当前机器数量: {machineCount}", 0);
                }
                catch (Exception ex)
                {
                    // 保留异常日志，方便排查
                    AddLogEntry($"[系统] 创建机器过滤按钮失败：{ex.Message}，异常堆栈：{ex.StackTrace}", 0, true);
                }
            });
        }

        /// <summary>
        /// 机器过滤按钮点击事件（新增）
        /// </summary>
        private void BtnMachineFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int machineId)
            {
                // 1. 更新当前过滤机器ID
                _currentFilterMachineId = machineId;

                // 2. 更新按钮样式（高亮当前选中）
                ResetFilterButtonStyles();
                btn.Background = (SolidColorBrush)FindResource("PrimaryColor");

                // 3. 更新统计数据和日志UI
                if (_allMachineData.TryGetValue(machineId, out MachineDataModel machineData))
                {
                    _currentDisplayData = machineData;
                }
                else
                {
                    _currentDisplayData = new MachineDataModel();
                }

                UpdateGlobalLogUI(); // 强制刷新日志
                StatsUpdateTimer_Tick(null, null); // 强制刷新统计

                AddLogEntry($"[系统] 已切换到「机器 {machineId}」的日志和统计", 0);
            }
        }

        /// <summary>
        /// 全部日志按钮点击事件（修改：重置固定日志类型）
        /// </summary>
        private void BtnAllLogs_Click(object sender, EventArgs e)
        {
            // 1. 重置过滤机器ID为0（全部），重置固定日志类型为全部
            _currentFilterMachineId = 0;
            _currentSelectedFixedLogType = FixedLogType.All;

            // 2. 更新按钮样式
            ResetFilterButtonStyles();
            btnAllLogs.Background = (SolidColorBrush)FindResource("PrimaryColor");

            // 3. 更新统计数据和日志UI
            if (_allMachineData.TryGetValue(0, out MachineDataModel globalData))
            {
                _currentDisplayData = globalData;
            }
            else
            {
                _currentDisplayData = new MachineDataModel();
            }

            UpdateGlobalLogUI();
            StatsUpdateTimer_Tick(null, null);

            AddLogEntry("[系统] 已切换到「全部日志」和全局统计", 0, false, FixedLogType.System);
        }

        /// <summary>
        /// 重置过滤按钮样式（辅助方法，修改：包含系统日志按钮）
        /// </summary>
        private void ResetFilterButtonStyles()
        {
            foreach (var child in spMachineFilterButtons.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = (SolidColorBrush)FindResource("SecondaryColor");
                }
            }
        }

        private void ResetAllStats()
        {
            lock (_requestCountLock)
            {
                _totalRequestsSent = 0;
                _totalSuccessfulRequests = 0;
            }

            _allMachineData.Clear();
            _allMachineData.Add(0, new MachineDataModel());

            _machineTotalRequests.Clear();
            _machineSuccessRequests.Clear();
            _errorStats.Clear();
            _responseTimeExtremes.Clear();
            _machineLastRequestTime.Clear();

            // 清空过滤日志列表
            _filteredLogList.Clear();
            Dispatcher.Invoke(() =>
            {
                lbGlobalLogs.Items.Clear();
            });
        }

        private void ClearLogs()
        {
            // 清空所有日志队列和列表
            _filteredLogList.Clear();
            ClearConcurrentQueue(_globalLogQueue);

            Dispatcher.Invoke(() =>
            {
                lbGlobalLogs.Items.Clear();
            });

            AddLogEntry("[系统] 日志已清空", 0);
        }

        private void ClearConcurrentQueue<TQueue>(ConcurrentQueue<TQueue> queue)
        {
            TQueue item;
            while (queue.TryDequeue(out item))
            {
            }
        }

        private void IncrementRequestCount(int machineId, bool isSuccess = false)
        {
            lock (_requestCountLock)
            {
                if (isSuccess)
                {
                    _totalSuccessfulRequests++;
                    _machineSuccessRequests.AddOrUpdate(machineId, 1, (k, v) => v + 1);
                }
                _totalRequestsSent++;
                _machineTotalRequests.AddOrUpdate(machineId, 1, (k, v) => v + 1);
            }
        }

        private async Task PollEndpoint(int machineId, string endpoint, HttpMethod requestMethod, CancellationToken cancellationToken)
        {
            AddLogEntry($"轮询任务已启动（间隔模式：{_requestIntervalSeconds}秒/次）", machineId);
            Dictionary<string, int> machineStats = new Dictionary<string, int>
            {
                { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                { "端口超限", 0 }
            };
            _errorStats[machineId] = machineStats;

            int maxRetryCount = 3;

            while (!cancellationToken.IsCancellationRequested && _isPolling)
            {
                if (IsPortUsageExceeded())
                {
                    machineStats["端口超限"]++;
                    AddLogEntry($"警告: 端口占用超限，暂停5秒", machineId);
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                int retryCount = 0;
                bool isRequestSuccess = false;

                while (retryCount < maxRetryCount && !isRequestSuccess)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 统一间隔控制（适配两种模式，_requestIntervalSeconds 已提前计算为毫秒）
                        await Task.Delay(_requestIntervalSeconds, cancellationToken).ConfigureAwait(false);

                        // 更新上次请求时间
                        _machineLastRequestTime[machineId] = DateTime.Now;

                        AddLogEntry($"发起请求...(重试：{retryCount})", machineId);
                        DateTime requestStartTime = DateTime.Now;
                        HttpResponseMessage response = null;
                        bool isRequestProcessed = false;

                        string jsonParameters = await Dispatcher.InvokeAsync(() => GetJsonParameters());

                        // 发送请求
                        if (requestMethod == HttpMethod.Get)
                        {
                            response = await _httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
                            isRequestProcessed = true;
                        }
                        else if (requestMethod == HttpMethod.Post)
                        {
                            if (!string.IsNullOrEmpty(jsonParameters))
                            {
                                var content = new StringContent(jsonParameters, Encoding.UTF8, "application/json");
                                response = await _httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                                isRequestProcessed = true;
                            }
                            else
                            {
                                AddLogEntry($"警告: POST请求无参数", machineId);
                                machineStats["参数错误"]++;
                                IncrementRequestCount(machineId, false);
                                isRequestSuccess = true;
                                break;
                            }
                        }

                        // 处理响应
                        if (isRequestProcessed && response != null)
                        {
                            string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            bool isSuccess = response.IsSuccessStatusCode;
                            TimeSpan responseTime = DateTime.Now - requestStartTime;
                            double responseTimeMs = responseTime.TotalMilliseconds;

                            // 更新响应时间极值
                            var currentExtremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                            if (_responseTimeExtremes.TryGetValue(machineId, out Tuple<double, double> storedExtremes))
                            {
                                currentExtremes = storedExtremes;
                            }
                            _responseTimeExtremes[machineId] = new Tuple<double, double>(
                                Math.Min(currentExtremes.Item1, responseTimeMs),
                                Math.Max(currentExtremes.Item2, responseTimeMs));

                            if (!isSuccess)
                            {
                                AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 状态码: {(int)response.StatusCode} | 结果: {result}", machineId);
                                machineStats["HTTP错误"]++;
                            }
                            else
                            {
                                AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 结果: {result}", machineId);
                                machineStats["成功次数"]++;
                            }

                            IncrementRequestCount(machineId, isSuccess);
                            isRequestSuccess = true;
                        }
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("每个套接字地址") || ex.Message.Contains("only use once"))
                    {
                        retryCount++;
                        machineStats["网络错误"]++;
                        AddLogEntry($"错误: 端口耗尽 - {ex.Message} (重试 {retryCount}/{maxRetryCount})", machineId, true);
                        IncrementRequestCount(machineId, false);
                        if (retryCount < maxRetryCount)
                        {
                            await Task.Delay(100 * retryCount, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            AddLogEntry($"轮询被取消", machineId);
                            break;
                        }
                        else
                        {
                            machineStats["超时"]++;
                            AddLogEntry($"错误: 请求超时（{_requestTimeoutSeconds}秒）", machineId, true);
                            IncrementRequestCount(machineId, false);
                        }
                        isRequestSuccess = true;
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        machineStats["网络错误"]++;
                        AddLogEntry($"错误: 网络异常 - {ex.Message}", machineId, true);
                        IncrementRequestCount(machineId, false);
                        retryCount = maxRetryCount;
                        break;
                    }
                    catch (Exception ex)
                    {
                        machineStats["其他错误"]++;
                        AddLogEntry($"错误: {ex.Message}", machineId, true);
                        IncrementRequestCount(machineId, false);
                        retryCount = maxRetryCount;
                        break;
                    }
                }
            }

            AddLogEntry($"轮询任务已停止", machineId);
        }

        private async void StartPolling_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // 更新请求间隔（从输入框读取）
                UpdateRequestConfigFromInput();

                UpdateMachineFilterButtons();
                await StartPollingAsync();
                SaveHistory();
            });
        }

        /// <summary>
        /// 重构：根据当前模式，读取输入框配置值并计算最终请求间隔
        /// </summary>
        private void UpdateRequestConfigFromInput()
        {
            int inputValue = 1; // 最小配置值为1
            if (txtPollInterval.Value.HasValue && int.TryParse(txtPollInterval.Value.Value.ToString(), out inputValue) && inputValue >= 1)
            {
                _requestConfigValue = inputValue;
            }
            else
            {
                // 无效值时默认1，更新输入框显示
                _requestConfigValue = 1;
                txtPollInterval.Value = 1;
                AddLogEntry($"[系统] 配置值无效（必须≥1），已设为默认值：1", 0, true);
            }

            // 核心：根据模式计算最终的「请求间隔毫秒数」（统一转换为毫秒，方便后续逻辑处理）
            if (_currentTestMode == TestMode.HighFrequency)
            {
                // 高频压测模式（一秒几次）：计算每次请求的间隔毫秒数
                // 例如：每秒5次 → 每次间隔 1000 / 5 = 200 毫秒
                _requestIntervalSeconds = (int)(1000 / (double)_requestConfigValue);
                // 防止配置值过大导致间隔为0（最小间隔10毫秒，避免请求过于密集）
                _requestIntervalSeconds = Math.Max(_requestIntervalSeconds, 10);
            }
            else
            {
                // 稳定监控模式（几秒一次）：直接转换为毫秒
                // 例如：3秒一次 → 3000 毫秒
                _requestIntervalSeconds = _requestConfigValue * 1000;
            }
        }

        private async Task StartPollingAsync()
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrWhiteSpace(txtEndpoint.Text))
                    {
                        AddLogEntry("[系统] 错误: Endpoint不能为空", 0, true);
                        MessageBox.Show("请输入有效的Endpoint地址", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new InvalidOperationException("Endpoint不能为空");
                    }

                    int tempMachineCount = 1;
                    if (!txtMachineCount.Value.HasValue || !int.TryParse(txtMachineCount.Value.Value.ToString(), out tempMachineCount) || tempMachineCount <= 0)
                    {
                        AddLogEntry("[系统] 机器数量无效", 0);
                        MessageBox.Show("机器数量必须是大于0的整数", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new InvalidOperationException("机器数量无效");
                    }

                    AddLogEntry($"[系统] 请求间隔: {_requestIntervalSeconds}秒/次 | 端口上限: {_maxPortUsage}", 0);
                });

                string endpoint = await Dispatcher.InvokeAsync(() => txtEndpoint.Text);
                HttpMethod requestMethod = _selectedHttpMethod;
                int machineCount = await Dispatcher.InvokeAsync(() =>
                {
                    int tempCount = 1;
                    if (txtMachineCount.Value.HasValue)
                    {
                        int.TryParse(txtMachineCount.Value.Value.ToString(), out tempCount);
                    }
                    return tempCount;
                });

                _pollingTasks.Clear();
                _isPolling = true;
                _cts = new CancellationTokenSource();

                _pollingStartTime = DateTime.Now;
                ResetAllStats();

                // 启动多机器轮询任务
                for (int i = 1; i <= machineCount; i++)
                {
                    int currentMachineId = i;
                    var task = Task.Run(() => PollEndpoint(currentMachineId, endpoint, requestMethod, _cts.Token), _cts.Token);
                    _pollingTasks.Add(task);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    btnStartPolling.IsEnabled = false;
                    AddLogEntry($"[系统] 轮询已开始. 机器数: {machineCount}, 请求间隔: {_requestIntervalSeconds}秒/次", 0);
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 启动轮询错误: {ex.Message}", 0, true);
                _isPolling = false;
            }
        }

        private async void StopPolling_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await StopPollingAsync();
                OutputErrorStatsSummary();
                ExportLogsToFile();
            });
        }

        private async Task StopPollingAsync()
        {
            if (_cts != null)
            {
                _isPolling = false;
                _cts.Cancel();

                try
                {
                    await Task.WhenAny(Task.WhenAll(_pollingTasks.ToArray()), Task.Delay(10000));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    AddLogEntry($"[系统] 停止轮询错误: {ex.Message}", 0, true);
                }
                finally
                {
                    if (_pollingStartTime != DateTime.MinValue)
                    {
                        TimeSpan totalTime = DateTime.Now - _pollingStartTime;
                        _totalPollingTime = totalTime;
                        AddLogEntry($"[系统] 轮询总时长: {totalTime:hh\\:mm\\:ss}", 0);
                    }

                    _cts.Dispose();
                    _cts = null;
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
            });

            AddLogEntry("[系统] 轮询已停止", 0);
            double successRate = 0;
            if (_totalRequestsSent > 0)
            {
                successRate = (double)_totalSuccessfulRequests / _totalRequestsSent * 100;
            }
            AddLogEntry($"[统计] 总请求: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {successRate:F2}%", 0);
        }

        /// <summary>
        /// txtPollInterval值变化事件（核心修改：更新请求间隔秒数）
        /// </summary>
        private void txtPollInterval_ValueChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<double?> e)
        {
            try
            {
                // 运行中修改也能实时生效
                UpdateRequestConfigFromInput();
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 更新请求间隔失败：{ex.Message}", 0, true);
            }
        }
        #endregion

        #region 辅助方法（修改日志中的频率描述为间隔）
        private int GetCurrentProcessTcpConnectionCount()
        {
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var connections = properties.GetActiveTcpConnections();
                int currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

                int count = 0;
                foreach (var conn in connections)
                {
                    if (conn.LocalEndPoint.Port >= 1024 &&
                        conn.LocalEndPoint.Port <= 65534 &&
                        conn.State != TcpState.TimeWait)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch (Exception ex)
            {
                AddLogEntry($"[监控] 获取端口数失败: {ex.Message}", 0);
                return 0;
            }
        }

        private bool IsPortUsageExceeded()
        {
            return _currentPortCount >= _maxPortUsage;
        }

        private string GetJsonParameters()
        {
            string parameters = txtParametersJson.Text;
            if (string.IsNullOrWhiteSpace(parameters)) return null;

            try
            {
                JsonConvert.DeserializeObject(parameters);
                return parameters;
            }
            catch (Exception ex)
            {
                AddLogEntry($"警告: JSON参数无效 - {ex.Message}", 0, true);
                return null;
            }
        }

        private void OutputErrorStatsSummary()
        {
            Dispatcher.Invoke(() =>
            {
                AddLogEntry("==================================== 统计汇总 ====================================", 0);
                TimeSpan totalTime = TimeSpan.Zero;
                if (_pollingStartTime != DateTime.MinValue)
                {
                    totalTime = DateTime.Now - _pollingStartTime;
                }
                string timeFormat = $"{totalTime.Hours:D2}:{totalTime.Minutes:D2}:{totalTime.Seconds:D2}.{totalTime.Milliseconds:D3}";

                AddLogEntry($"[全局] 总时长: {timeFormat} | 请求间隔: {_requestIntervalSeconds}秒/次 | 端口上限: {_maxPortUsage}", 0);
                double globalSuccessRate = 0;
                if (_totalRequestsSent > 0)
                {
                    globalSuccessRate = (double)_totalSuccessfulRequests / _totalRequestsSent * 100;
                }
                AddLogEntry($"[全局] 总请求: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {globalSuccessRate:F2}%", 0);
                AddLogEntry($"[全局] 最终端口占用: {_currentPortCount}/{_maxPortUsage}", 0);

                foreach (var kvp in _machineTotalRequests)
                {
                    int machineId = kvp.Key;
                    Dictionary<string, int> stats = new Dictionary<string, int>();
                    if (_errorStats.TryGetValue(machineId, out Dictionary<string, int> storedStats))
                    {
                        stats = storedStats;
                    }

                    Tuple<double, double> extremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                    if (_responseTimeExtremes.TryGetValue(machineId, out Tuple<double, double> storedExtremes))
                    {
                        extremes = storedExtremes;
                    }

                    int machineTotal = kvp.Value;
                    int machineSuccess = 0;
                    if (_machineSuccessRequests.TryGetValue(machineId, out int storedMachineSuccess))
                    {
                        machineSuccess = storedMachineSuccess;
                    }

                    double successRate = machineTotal > 0 ? (double)machineSuccess / machineTotal * 100 : 0;

                    string minTime = "无数据";
                    string maxTime = "无数据";
                    if (machineSuccess > 0)
                    {
                        minTime = $"{extremes.Item1:F2}ms";
                        maxTime = $"{extremes.Item2:F2}ms";
                    }

                    int timeoutCount = 0;
                    if (stats.TryGetValue("超时", out int storedTimeoutCount))
                    {
                        timeoutCount = storedTimeoutCount;
                    }

                    int networkErrorCount = 0;
                    if (stats.TryGetValue("网络错误", out int storedNetworkErrorCount))
                    {
                        networkErrorCount = storedNetworkErrorCount;
                    }

                    int httpErrorCount = 0;
                    if (stats.TryGetValue("HTTP错误", out int storedHttpErrorCount))
                    {
                        httpErrorCount = storedHttpErrorCount;
                    }

                    string summary = $"机器 {machineId} | 总请求: {machineTotal} | 成功: {machineSuccess} ({successRate:F2}%) | 最短响应: {minTime} | 最长响应: {maxTime} | 超时: {timeoutCount} | 网络错误: {networkErrorCount} | HTTP错误: {httpErrorCount}";
                    AddLogEntry(summary, 0);
                }
                AddLogEntry("=================================================================================", 0);
            });
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            ClearLogs();
        }
        /// <summary>
        /// 加载最新历史记录按钮点击事件（重新读取磁盘文件，刷新ListBox和界面控件）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool _isLoadingHistory = false; // 防重复执行锁
        private void LoadFromHistory_Click(object sender, EventArgs e)
        {
            // 防重复执行：如果正在加载，直接返回，避免重复弹窗和逻辑
            if (_isLoadingHistory)
            {
                return;
            }

            try
            {
                _isLoadingHistory = true; // 标记开始执行

                // 1. 统一历史文件路径（和SaveHistory保持一致）
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");

                // 2. 检查文件是否存在
                if (!File.Exists(historyPath))
                {
                    AddLogEntry("[系统] 无历史记录文件可加载（未找到history.json）", 0, true);
                    MessageBox.Show("未找到历史记录文件，请先保存过配置再尝试", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 3. 重新从磁盘读取文件内容（无缓存，确保最新）
                string existingContent = string.Empty;
                existingContent = ReadFileContentWithRetry(historyPath);

                // 4. 检查文件内容是否有效
                if (string.IsNullOrWhiteSpace(existingContent))
                {
                    AddLogEntry("[系统] 历史记录文件内容为空，无法加载", 0, true);
                    MessageBox.Show("历史记录文件内容无效，无法加载", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 5. 反序列化为HistoryRoot（适配数组格式，容错处理）
                HistoryRoot historyRoot = null;
                historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(existingContent);

                if (historyRoot == null || historyRoot.HistoryList == null || historyRoot.HistoryList.Count == 0)
                {
                    AddLogEntry("[系统] 历史记录文件中无有效配置项", 0, true);
                    MessageBox.Show("历史记录中没有可加载的配置", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 6. 获取最新一条历史记录（按保存时间倒序，确保取到最新）
                HistoryItem latestHistoryItem = historyRoot.HistoryList
                    .OrderByDescending(item => item.SavedTime)
                    .FirstOrDefault();

                if (latestHistoryItem == null)
                {
                    AddLogEntry("[系统] 无法提取最新的历史记录", 0, true);
                    MessageBox.Show("无法获取有效最新历史记录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 7. 切换到UI线程，更新所有控件和ListBox
                Dispatcher.Invoke(() =>
                {
                    // ===== 第一步：刷新ListBox（lbHistory），同步最新历史记录（倒序，最新在前） =====
                    var sortedHistoryList = historyRoot.HistoryList
                        .OrderByDescending(item => item.SavedTime)
                        .ToList();

                    // 先清空原有绑定，避免WPF列表刷新异常
                    lbHistory.ItemsSource = null;
                    // 重新绑定排序后的列表
                    lbHistory.ItemsSource = sortedHistoryList;
                    // 自动选中最新一条记录，保持界面状态统一
                    if (sortedHistoryList.Count > 0)
                    {
                        lbHistory.SelectedItem = latestHistoryItem;
                    }

                    // ===== 第二步：基础输入框赋值（兼容空值） =====
                    txtEndpoint.Text = latestHistoryItem.Endpoint ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(latestHistoryItem.MachineCount) && double.TryParse(latestHistoryItem.MachineCount, out double machineCount))
                    {
                        txtMachineCount.Value = machineCount;
                    }
                    else
                    {
                        txtMachineCount.Value = 1;
                    }

                    if (!string.IsNullOrWhiteSpace(latestHistoryItem.PollInterval) && double.TryParse(latestHistoryItem.PollInterval, out double pollInterval))
                    {
                        txtPollInterval.Value = pollInterval;
                    }
                    else
                    {
                        txtPollInterval.Value = 1;
                    }

                    txtParametersJson.Text = latestHistoryItem.ParametersJson ?? string.Empty;

                    // ===== 第三步：恢复Http请求方法下拉框（cboHttpMethod） =====
                    string httpMethod = latestHistoryItem.HttpMethod ?? "GET";
                    bool isHttpMethodMatched = false;

                    foreach (ComboBoxItem item in cboHttpMethod.Items)
                    {
                        string itemTag = item.Tag?.ToString() ?? "";
                        if (string.Equals(itemTag, httpMethod, StringComparison.OrdinalIgnoreCase))
                        {
                            cboHttpMethod.SelectedItem = item;
                            _selectedHttpMethod = itemTag.ToUpper() == "GET" ? HttpMethod.Get : HttpMethod.Post;
                            isHttpMethodMatched = true;
                            break;
                        }
                    }

                    if (!isHttpMethodMatched)
                    {
                        cboHttpMethod.SelectedIndex = 0;
                        _selectedHttpMethod = HttpMethod.Get;
                    }

                    // ===== 第四步：恢复测试模式下拉框（cboTestMode） =====
                    string testMode = latestHistoryItem.TestMode ?? "HighFrequency";
                    bool isModeMatched = false;

                    foreach (ComboBoxItem item in cboTestMode.Items)
                    {
                        string itemTag = item.Tag?.ToString() ?? "";
                        if (string.Equals(itemTag, testMode, StringComparison.OrdinalIgnoreCase))
                        {
                            cboTestMode.SelectedItem = item;
                            CboTestMode_SelectionChanged(cboTestMode, null);
                            isModeMatched = true;
                            break;
                        }
                    }

                    if (!isModeMatched)
                    {
                        cboTestMode.SelectedIndex = 0;
                        CboTestMode_SelectionChanged(cboTestMode, null);
                    }

                    // ===== 第五步：更新请求配置值，确保逻辑一致 =====
                    if (!string.IsNullOrWhiteSpace(latestHistoryItem.RequestConfigValue) && int.TryParse(latestHistoryItem.RequestConfigValue, out int configValue))
                    {
                        _requestConfigValue = configValue;
                        UpdateRequestConfigFromInput();
                    }
                });

                // 8. 输出日志并弹出成功提示
                AddLogEntry($"[系统] 已成功加载最新历史记录（保存时间：{latestHistoryItem.SavedTime:yyyy-MM-dd HH:mm:ss}）", 0);
                MessageBox.Show("最新历史记录加载完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (JsonSerializationException ex)
            {
                // 专门捕获反序列化格式错误（最常见）
                AddLogEntry($"[系统] 加载历史记录失败：配置文件格式不匹配 - {ex.Message}", 0, true);
                MessageBox.Show("历史记录文件格式错误，无法加载\n请检查history.json文件是否完整", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // 捕获其他所有异常，避免程序崩溃
                AddLogEntry($"[系统] 加载历史记录失败：{ex.Message}", 0, true);
                MessageBox.Show($"加载历史记录出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 无论成功还是失败，都标记执行结束，允许下次点击
                _isLoadingHistory = false;
            }
        }

        /// <summary>
        /// Http请求方法下拉框切换事件（更新全局变量_selectedHttpMethod）
        /// </summary>
        private void CboHttpMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cboHttpMethod.SelectedItem is ComboBoxItem selectedItem)
                {
                    string methodTag = selectedItem.Tag?.ToString() ?? "GET";
                    // 更新全局变量，确保后续请求使用正确的方法
                    _selectedHttpMethod = methodTag.ToUpper() == "GET" ? HttpMethod.Get : HttpMethod.Post;

                    // 可选：输出日志，方便调试
                    AddLogEntry($"[系统] 已切换请求方法：{_selectedHttpMethod.Method}", 0);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 切换请求方法失败：{ex.Message}", 0, true);
            }
        }

        //private void LoadFromHistory_Click(object sender, EventArgs e)
        //{
        //    try
        //    {
        //        // 1. 统一文件路径（和保存历史的路径一致，指向history.json）
        //        string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");

        //        // 2. 先判断文件是否存在，避免「文件未找到」异常
        //        if (!File.Exists(historyPath))
        //        {
        //            AddLogEntry("[系统] 无历史记录文件可加载（未找到history.json）", 0, true);
        //            MessageBox.Show("未找到历史记录文件，请先保存过配置再尝试", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        //            return;
        //        }

        //        // 3. 读取文件内容（复用你已有的重试读取方法，提高稳定性）
        //        string existingContent = ReadFileContentWithRetry(historyPath);
        //        if (string.IsNullOrWhiteSpace(existingContent))
        //        {
        //            AddLogEntry("[系统] 历史记录文件内容为空，无法加载", 0, true);
        //            MessageBox.Show("历史记录文件内容无效，无法加载", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        //            return;
        //        }

        //        // 4. 反序列化为HistoryRoot（适配现有数组格式），避免格式不匹配异常
        //        HistoryRoot historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(existingContent);
        //        if (historyRoot == null || historyRoot.HistoryList == null || historyRoot.HistoryList.Count == 0)
        //        {
        //            AddLogEntry("[系统] 历史记录文件中无有效配置项", 0, true);
        //            MessageBox.Show("历史记录中没有可加载的配置", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        //            return;
        //        }

        //        // 5. 取最新一条历史记录（按保存时间倒序，取第一条）
        //        HistoryItem latestHistoryItem = historyRoot.HistoryList
        //            .OrderByDescending(item => item.SavedTime)
        //            .FirstOrDefault();
        //        if (latestHistoryItem == null)
        //        {
        //            AddLogEntry("[系统] 无法获取最新的历史记录", 0, true);
        //            MessageBox.Show("无法提取有效历史记录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        //            return;
        //        }

        //        // 6. 切换到UI线程，更新所有控件状态（避免跨线程访问异常）
        //        Dispatcher.Invoke(() =>
        //        {
        //            // ===== 基础输入框赋值 =====
        //            txtEndpoint.Text = latestHistoryItem.Endpoint ?? string.Empty;
        //            // 兼容空值，避免转换失败
        //            if (!string.IsNullOrWhiteSpace(latestHistoryItem.MachineCount) && double.TryParse(latestHistoryItem.MachineCount, out double machineCount))
        //            {
        //                txtMachineCount.Value = machineCount;
        //            }
        //            else
        //            {
        //                txtMachineCount.Value = 1;
        //            }

        //            if (!string.IsNullOrWhiteSpace(latestHistoryItem.PollInterval) && double.TryParse(latestHistoryItem.PollInterval, out double pollInterval))
        //            {
        //                txtPollInterval.Value = pollInterval;
        //            }
        //            else
        //            {
        //                txtPollInterval.Value = 1;
        //            }

        //            txtParametersJson.Text = latestHistoryItem.ParametersJson ?? string.Empty;

        //            // ===== 恢复HttpMethod单选按钮（Get/Post） =====
        //            string httpMethod = latestHistoryItem.HttpMethod ?? "GET";
        //            switch (httpMethod.ToUpper())
        //            {
        //                case "GET":
        //                    rbGet.IsChecked = true;
        //                    rbPost.IsChecked = false;
        //                    _selectedHttpMethod = HttpMethod.Get;
        //                    break;
        //                case "POST":
        //                    rbPost.IsChecked = true;
        //                    rbGet.IsChecked = false;
        //                    _selectedHttpMethod = HttpMethod.Post;
        //                    break;
        //                default:
        //                    rbGet.IsChecked = true;
        //                    _selectedHttpMethod = HttpMethod.Get;
        //                    break;
        //            }

        //            // ===== 恢复测试模式下拉框（cboTestMode） =====
        //            string testMode = latestHistoryItem.TestMode ?? "HighFrequency";
        //            bool isModeMatched = false;
        //            foreach (ComboBoxItem item in cboTestMode.Items)
        //            {
        //                string itemTag = item.Tag?.ToString() ?? "";
        //                if (string.Equals(itemTag, testMode, StringComparison.OrdinalIgnoreCase))
        //                {
        //                    cboTestMode.SelectedItem = item;
        //                    CboTestMode_SelectionChanged(cboTestMode, null);
        //                    isModeMatched = true;
        //                    break;
        //                }
        //            }

        //            // 未匹配到有效模式，默认选中高频压测
        //            if (!isModeMatched)
        //            {
        //                cboTestMode.SelectedIndex = 0;
        //                CboTestMode_SelectionChanged(cboTestMode, null);
        //            }
        //        });

        //        // 7. 输出日志，提示加载成功
        //        AddLogEntry($"[系统] 已成功加载最新历史记录（保存时间：{latestHistoryItem.SavedTime:yyyy-MM-dd HH:mm:ss}）", 0);
        //        MessageBox.Show("最新历史记录加载完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        //    }
        //    catch (JsonSerializationException ex)
        //    {
        //        // 专门捕获反序列化格式错误（最常见报错）
        //        AddLogEntry($"[系统] 加载历史记录失败：配置文件格式不匹配 - {ex.Message}", 0, true);
        //        MessageBox.Show("历史记录文件格式错误，无法加载\n请检查history.json文件是否完整", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //    catch (Exception ex)
        //    {
        //        // 捕获其他所有异常，避免程序崩溃
        //        AddLogEntry($"[系统] 加载历史记录失败：{ex.Message}", 0, true);
        //        MessageBox.Show($"加载历史记录出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}

        private void txtMachineCount_ValueChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<double?> e)
        {
            try
            {
                if (e.NewValue.HasValue && e.NewValue.Value > 0)
                {
                    UpdateMachineFilterButtons();
                    AddLogEntry($"[系统] 机器数量已更新为：{(int)e.NewValue.Value}", 0);
                }
                else if (e.NewValue.HasValue && e.NewValue.Value <= 0)
                {
                    AddLogEntry("[系统] 机器数量不能小于等于0，已忽略无效值", 0, true);
                    Dispatcher.Invoke(() =>
                    {
                        txtMachineCount.Value = 1;
                    });
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 更新机器数量失败：{ex.Message}", 0, true);
            }
        }

        private void HandleUnhandledException(Exception ex, string exceptionType)
        {
            if (ex == null) return;

            string errorMsg = $"[{exceptionType}] 异常: {ex.Message}\n堆栈: {ex.StackTrace}";
            AddLogEntry(errorMsg, 0, true);

            EmergencySaveLog();

            MessageBox.Show($"程序异常: {ex.Message}\n日志已紧急保存",
                "异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void EmergencySaveLog()
        {
            lock (_logLock)
            {
                if (!_isLogInitialized) return;

                try
                {
                    string crashFileName = $"CrashLog_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
                    string crashFilePath = Path.Combine(_logDirectory, crashFileName);

                    if (File.Exists(_tempLogFilePath))
                    {
                        CopyFileWithRetry(_tempLogFilePath, crashFilePath);
                    }

                    using (StreamWriter sw = new StreamWriter(crashFilePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine();
                        sw.WriteLine("==================== 程序崩溃信息 ====================");
                        sw.WriteLine($"崩溃时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        sw.WriteLine($"程序版本: {AppDomain.CurrentDomain.FriendlyName}");
                        sw.WriteLine($"操作系统: {Environment.OSVersion.VersionString}");
                        sw.WriteLine($"CPU核心数: {Environment.ProcessorCount}");
                        sw.WriteLine($"请求间隔: {_requestIntervalSeconds}秒/次");
                        sw.WriteLine($"端口占用: {_currentPortCount}/{_maxPortUsage}");
                        sw.WriteLine($"总请求数: {_totalRequestsSent}");
                        sw.WriteLine($"======================================================");
                        sw.Flush();
                    }

                    AddLogEntry($"[系统] 崩溃日志已保存: {crashFilePath}", 0);

                    try
                    {
                        DeleteFileWithRetry(_tempLogFilePath);
                    }
                    catch (Exception ex)
                    {
                        AddLogEntry($"[系统] 删除临时日志失败: {ex.Message}", 0);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        string lastLog = Path.Combine(_logDirectory, "LastResortCrash.log");
                        File.WriteAllText(lastLog, $"紧急保存失败: {ex.Message}", Encoding.UTF8);
                    }
                    catch { }
                }
            }
        }

        private void CopyFileWithRetry(string sourcePath, string destPath)
        {
            int retryCount = 0;
            while (retryCount < FileOperationRetryCount)
            {
                try
                {
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        sourceStream.CopyTo(destStream);
                    }
                    return;
                }
                catch (IOException)
                {
                    retryCount++;
                    if (retryCount >= FileOperationRetryCount) throw;
                    Thread.Sleep(FileOperationRetryDelay);
                }
            }
        }

        private void DeleteFileWithRetry(string filePath)
        {
            int retryCount = 0;
            while (retryCount < FileOperationRetryCount)
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    return;
                }
                catch (IOException)
                {
                    retryCount++;
                    if (retryCount >= FileOperationRetryCount) throw;
                    Thread.Sleep(FileOperationRetryDelay);
                }
            }
        }

        private string ReadFileContentWithRetry(string filePath)
        {
            int retryCount = 0;
            while (retryCount < FileOperationRetryCount)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException)
                {
                    retryCount++;
                    if (retryCount >= FileOperationRetryCount) throw;
                    Thread.Sleep(FileOperationRetryDelay);
                }
            }
            return string.Empty;
        }

        private void ExportLogsToFile()
        {
            lock (_logLock)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string finalFileName = $"PollingLog_{timestamp}.txt";
                    string finalFilePath = Path.Combine(_logDirectory, finalFileName);

                    string tempContent = string.Empty;
                    if (File.Exists(_tempLogFilePath))
                    {
                        tempContent = ReadFileContentWithRetry(_tempLogFilePath);
                    }

                    StringBuilder finalContent = new StringBuilder();
                    finalContent.AppendLine("==================== 轮询测试日志 ====================");
                    finalContent.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                    Dispatcher.Invoke(() =>
                    {
                        finalContent.AppendLine($"Endpoint: {txtEndpoint.Text}");
                        finalContent.AppendLine($"机器数量: {txtMachineCount.Value}");
                    });

                    finalContent.AppendLine($"请求方法: {_selectedHttpMethod.Method}");
                    finalContent.AppendLine($"请求间隔: {_requestIntervalSeconds}秒/次");
                    finalContent.AppendLine($"端口上限: {_maxPortUsage}");
                    double exportSuccessRate = _totalRequestsSent > 0 ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100 : 0;
                    finalContent.AppendLine($"总请求数: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {exportSuccessRate:F2}%");
                    finalContent.AppendLine("======================================================");
                    finalContent.AppendLine();
                    finalContent.AppendLine(tempContent);

                    using (var fs = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.Write(finalContent.ToString());
                        sw.Flush();
                    }

                    AddLogEntry($"[系统] 日志已导出: {finalFilePath}", 0);

                    try
                    {
                        DeleteFileWithRetry(_tempLogFilePath);
                    }
                    catch (Exception ex)
                    {
                        AddLogEntry($"[系统] 删除临时日志失败: {ex.Message}", 0);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        MessageBoxResult result = MessageBox.Show(
                            $"日志已保存！\n路径：{finalFilePath}\n\n是否打开文件夹？",
                            "导出成功",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{finalFilePath}\"");
                        }
                    });
                }
                catch (Exception ex)
                {
                    AddLogEntry($"[系统] 导出日志失败: {ex.Message}", 0, true);
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"导出失败：{ex.Message}",
                            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
        }

        /// 重构：添加带机器标记和固定日志类型的日志条目（核心修改）
        /// </summary>
        /// <param name="message">日志内容</param>
        /// <param name="machineId">机器ID（0=无机器）</param>
        /// <param name="isError">是否为错误日志</param>
        /// <param name="logType">固定日志类型（默认：业务机器日志，可指定系统/硬件）</param>
        private void AddLogEntry(string message, int machineId = 0, bool isError = false, FixedLogType logType = FixedLogType.BusinessMachine)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string prefix = machineId == 0 ? message : $"[机器 {machineId,2}] {message}";
            string fullMessage = $"[{timestamp}] {prefix}";

            // 入队带标记和固定日志类型的日志条目
            _globalLogQueue.Enqueue(new LogEntry
            {
                Message = fullMessage,
                MachineId = machineId,
                IsError = isError,
                Timestamp = DateTime.Now,
                LogType = logType // 赋值固定日志类型，用于过滤
            });

            // 异步写入文件
            _ = WriteLogToFile(fullMessage, isError);
        }

        private async Task WriteLogToFile(string message, bool isError = false)
        {
            if (!_isLogInitialized) return;

            int retryCount = 0;
            while (retryCount < FileOperationRetryCount)
            {
                FileStream fs = null;
                StreamWriter sw = null;
                try
                {
                    fs = new FileStream(_tempLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true);
                    sw = new StreamWriter(fs, Encoding.UTF8);

                    await sw.WriteLineAsync(message);
                    await sw.FlushAsync();

                    return;
                }
                catch (IOException)
                {
                    retryCount++;
                    if (retryCount >= FileOperationRetryCount)
                    {
                        await WriteToFallbackLog(message);
                        return;
                    }
                    await Task.Delay(FileOperationRetryDelay);
                }
                finally
                {
                    if (sw != null) sw.Dispose();
                    if (fs != null) fs.Dispose();
                }
            }
        }

        private async Task WriteToFallbackLog(string message)
        {
            try
            {
                string fallbackLogPath = Path.Combine(_logDirectory, "Fallback_Polling.log");
                using (var fs = new FileStream(fallbackLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    await sw.WriteLineAsync(message);
                    await sw.FlushAsync();
                }
            }
            catch
            {
            }
        }

        private void LoadHistory()
        {
            try
            {
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
                if (File.Exists(historyPath))
                {
                    string content = ReadFileContentWithRetry(historyPath);
                    var historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(content);
                    if (historyRoot != null && historyRoot.HistoryList != null)
                    {
                        // ===== 关键：加载后，按时间倒序排序（最新的记录在列表最前面） =====
                        var sortedHistoryList = historyRoot.HistoryList
                            .OrderByDescending(item => item.SavedTime)
                            .ToList();

                        // 绑定倒序后的列表到UI
                        Dispatcher.Invoke(() =>
                        {
                            lbHistory.ItemsSource = sortedHistoryList;
                        });

                        AddLogEntry("[系统] 历史配置加载成功（最新记录在前）", 0);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 加载历史配置失败：{ex.Message}", 0);
            }
        }

        private void SaveHistory()
        {
            try
            {
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
                HistoryRoot historyRoot = new HistoryRoot();

                if (File.Exists(historyPath))
                {
                    string existingContent = ReadFileContentWithRetry(historyPath);
                    historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(existingContent) ?? new HistoryRoot();
                }

                // 构建新记录
                HistoryItem newHistoryItem = new HistoryItem
                {
                    Endpoint = txtEndpoint.Text,
                    MachineCount = txtMachineCount.Value?.ToString() ?? "1",
                    PollInterval = txtPollInterval.Value?.ToString() ?? "1",
                    ParametersJson = txtParametersJson.Text,
                    HttpMethod = _selectedHttpMethod.Method,
                    TestMode = _currentTestMode.ToString(),
                    RequestConfigValue = _requestConfigValue.ToString(),
                    SavedTime = DateTime.Now
                };

                // 追加新记录
                historyRoot.HistoryList.Add(newHistoryItem);

                // 限制最大记录数
                const int maxHistoryCount = 10;
                if (historyRoot.HistoryList.Count > maxHistoryCount)
                {
                    historyRoot.HistoryList.RemoveRange(0, historyRoot.HistoryList.Count - maxHistoryCount);
                }

                // ===== 关键：保存前，按时间倒序排序（最新的记录在列表最前面） =====
                historyRoot.HistoryList = historyRoot.HistoryList
                    .OrderByDescending(item => item.SavedTime)
                    .ToList();

                // 序列化并写入文件
                string content = JsonConvert.SerializeObject(historyRoot, Formatting.Indented);
                using (var fs = new FileStream(historyPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.Write(content);
                    sw.Flush();
                }

                // ===== 额外：更新列表UI，确保列表也显示为倒序 =====
                Dispatcher.Invoke(() =>
                {
                    lbHistory.ItemsSource = null; // 先清空列表绑定
                    lbHistory.ItemsSource = historyRoot.HistoryList; // 重新绑定倒序后的列表
                });

                AddLogEntry("[系统] 当前配置已保存到历史记录（追加新条目，最新在前）", 0);
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 保存历史配置失败：{ex.Message}", 0);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                _isPolling = false;
                // 停止业务轮询
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
                // 停止硬件轮询（调用专属方法，操作专属Cts）
                StopHardwarePolling();

                if (_logUiTimer != null) _logUiTimer.Stop();
                if (_portMonitorTimer != null) _portMonitorTimer.Stop();
                if (_statsUpdateTimer != null) _statsUpdateTimer.Stop();

                if (_httpClient != null) _httpClient.Dispose();

                ExportLogsToFile();

                AddLogEntry("[系统] 程序正常退出", 0);
            }
            catch (Exception ex)
            {
                HandleUnhandledException(ex, "窗口关闭异常");
            }
        }

        private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
            });
        }

        /// <summary>
        /// 历史记录列表选中项变更事件（加载选中的历史记录到界面，适配下拉框）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LbHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 确保在UI线程执行，避免跨线程访问控件异常
            Dispatcher.Invoke(() =>
            {
                // 1. 判断是否选中了有效历史项
                if (lbHistory.SelectedItem is HistoryItem selectedItem)
                {
                    // ===== 步骤1：基础输入框赋值（兼容空值，避免转换报错） =====
                    txtEndpoint.Text = selectedItem.Endpoint ?? string.Empty;

                    // 机器数赋值（容错处理）
                    if (!string.IsNullOrWhiteSpace(selectedItem.MachineCount) && double.TryParse(selectedItem.MachineCount, out double machineCount))
                    {
                        txtMachineCount.Value = machineCount;
                    }
                    else
                    {
                        txtMachineCount.Value = 1;
                    }

                    // 配置值（间隔/频率）赋值（容错处理）
                    if (!string.IsNullOrWhiteSpace(selectedItem.PollInterval) && double.TryParse(selectedItem.PollInterval, out double pollInterval))
                    {
                        txtPollInterval.Value = pollInterval;
                    }
                    else
                    {
                        txtPollInterval.Value = 1;
                    }

                    // 请求参数赋值
                    txtParametersJson.Text = selectedItem.ParametersJson ?? string.Empty;

                    // ===== 步骤2：恢复Http请求方法下拉框（cboHttpMethod） =====
                    string httpMethod = selectedItem.HttpMethod ?? "GET";
                    bool isHttpMethodMatched = false;

                    // 遍历下拉框选项，匹配历史记录的请求方法
                    foreach (ComboBoxItem item in cboHttpMethod.Items)
                    {
                        string itemTag = item.Tag?.ToString() ?? "";
                        if (string.Equals(itemTag, httpMethod, StringComparison.OrdinalIgnoreCase))
                        {
                            cboHttpMethod.SelectedItem = item;
                            // 更新全局变量，确保后续请求方法正确
                            _selectedHttpMethod = itemTag.ToUpper() == "GET" ? HttpMethod.Get : HttpMethod.Post;
                            isHttpMethodMatched = true;
                            break;
                        }
                    }

                    // 未匹配到有效方法，默认选中GET（兜底容错）
                    if (!isHttpMethodMatched)
                    {
                        cboHttpMethod.SelectedIndex = 0;
                        _selectedHttpMethod = HttpMethod.Get;
                    }

                    // ===== 步骤3：恢复测试模式下拉框（cboTestMode） =====
                    string testMode = selectedItem.TestMode ?? "HighFrequency";
                    bool isModeMatched = false;

                    foreach (ComboBoxItem item in cboTestMode.Items)
                    {
                        string itemTag = item.Tag?.ToString() ?? "";
                        if (string.Equals(itemTag, testMode, StringComparison.OrdinalIgnoreCase))
                        {
                            cboTestMode.SelectedItem = item;
                            // 主动触发模式切换事件，更新配置描述和请求间隔
                            CboTestMode_SelectionChanged(cboTestMode, null);
                            isModeMatched = true;
                            break;
                        }
                    }

                    // 未匹配到有效模式，默认选中高频压测（兜底容错）
                    if (!isModeMatched)
                    {
                        cboTestMode.SelectedIndex = 0;
                        CboTestMode_SelectionChanged(cboTestMode, null);
                    }

                    // ===== 步骤4：额外更新请求配置值（确保逻辑一致） =====
                    if (!string.IsNullOrWhiteSpace(selectedItem.RequestConfigValue) && int.TryParse(selectedItem.RequestConfigValue, out int configValue))
                    {
                        _requestConfigValue = configValue;
                        UpdateRequestConfigFromInput();
                    }

                    // ===== 步骤5：输出日志，记录加载结果 =====
                    AddLogEntry($"[系统] 已加载选中的历史记录：{selectedItem.Endpoint}（请求方法：{_selectedHttpMethod.Method}）", 0);
                }
            });
        }
        #endregion

        #region INotifyPropertyChanged 实现（保持不变）
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int CurrentPortCount
        {
            get => _currentPortCount;
            set
            {
                _currentPortCount = value;
                OnPropertyChanged(nameof(CurrentPortCount));
            }
        }

        public string RunTime
        {
            get
            {
                if (_pollingStartTime == DateTime.MinValue || !_isPolling)
                    return "00:00:00";
                TimeSpan runTime = DateTime.Now - _pollingStartTime;
                return $"{runTime.Hours:D2}:{runTime.Minutes:D2}:{runTime.Seconds:D2}";
            }
        }

        public bool IsPolling
        {
            get => _isPolling;
            set
            {
                _isPolling = value;
                OnPropertyChanged(nameof(IsPolling));
            }
        }
        #endregion

        /// <summary>
        /// 独立按钮：删除当前 ListBox 选中的历史记录（通过 Guid 精准匹配）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnDeleteSelectedHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 第一步：判断 ListBox 是否有选中项（避免空操作报错）
                if (lbHistory.SelectedItem == null || !(lbHistory.SelectedItem is HistoryItem selectedItemToDelete))
                {
                    AddLogEntry("[系统] 未选中任何要删除的历史记录", 0, true);
                    MessageBox.Show("请先在列表中选中一条要删除的历史记录！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2. 第二步：弹出确认框，防止误删（推荐保留，提升安全性）
                // 显示自定义名称（如果有）+ 接口地址，更直观
                string recordInfo = string.IsNullOrEmpty(selectedItemToDelete.CustomName)
                    ? selectedItemToDelete.Endpoint
                    : $"{selectedItemToDelete.CustomName}（{selectedItemToDelete.Endpoint}）";
                var confirmResult = MessageBox.Show($"确定要删除这条历史记录吗？\n{recordInfo}", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirmResult != MessageBoxResult.Yes)
                {
                    return; // 用户取消删除，直接返回
                }

                // 3. 第三步：读取磁盘中的 history.json 历史文件
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
                HistoryRoot historyRoot = new HistoryRoot(); // 初始化空的历史记录容器

                if (File.Exists(historyPath))
                {
                    // 读取文件内容并反序列化
                    string existingContent = ReadFileContentWithRetry(historyPath);
                    if (!string.IsNullOrWhiteSpace(existingContent))
                    {
                        historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(existingContent) ?? new HistoryRoot();
                    }
                }

                // 4. 第四步：通过 Guid（Id）精准查找并移除要删除的记录（解决引用不匹配问题）
                // 查找匹配 Id 的记录
                HistoryItem itemToRemove = historyRoot.HistoryList.FirstOrDefault(item => item.Id == selectedItemToDelete.Id) as HistoryItem;
                if (itemToRemove == null)
                {
                    AddLogEntry($"[系统] 删除失败：未在历史文件中找到该记录（Id：{selectedItemToDelete.Id}）", 0, true);
                    MessageBox.Show("删除失败，未在历史文件中找到该条记录！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 移除找到的记录
                historyRoot.HistoryList.Remove(itemToRemove);

                // 5. 第五步：将更新后的历史列表覆盖写入磁盘（同步文件）
                string updatedHistoryContent = JsonConvert.SerializeObject(historyRoot, Formatting.Indented);
                using (var fileStream = new FileStream(historyPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var streamWriter = new StreamWriter(fileStream, Encoding.UTF8))
                {
                    streamWriter.Write(updatedHistoryContent);
                    streamWriter.Flush();
                }

                // 6. 第六步：刷新 ListBox 界面（倒序排列，最新在前，清除选中状态）
                Dispatcher.Invoke(() =>
                {
                    // 对更新后的列表倒序排序（保持最新记录在前面）
                    var sortedUpdatedList = historyRoot.HistoryList
                        .OrderByDescending(item => item.SavedTime)
                        .ToList();

                    // 刷新 ListBox 绑定（先清空再绑定，避免 WPF 刷新异常）
                    lbHistory.ItemsSource = null;
                    lbHistory.ItemsSource = sortedUpdatedList;

                    // 清除选中状态（删除后无选中项，界面更整洁）
                    lbHistory.SelectedItem = null;
                });

                // 7. 第七步：输出日志并提示删除成功
                AddLogEntry($"[系统] 已成功删除选中的历史记录（Id：{selectedItemToDelete.Id}）", 0);
                MessageBox.Show("历史记录删除成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // 捕获所有异常，避免程序崩溃
                AddLogEntry($"[系统] 删除选中历史记录失败：{ex.Message}", 0, true);
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 编辑按钮点击：显示 Popup 编辑面板，填充所有字段数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <summary>
        /// 编辑按钮点击：显示 Popup 编辑面板，填充所有字段数据，重置居中位置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnEditSelectedHistory_Click(object sender, RoutedEventArgs e)
        {
            // 1. 判断是否有选中项
            if (lbHistory.SelectedItem == null || !(lbHistory.SelectedItem is HistoryItem selectedItemToEdit))
            {
                AddLogEntry("[系统] 未选中任何要编辑的历史记录", 0, true);
                MessageBox.Show("请先在列表中选中一条要编辑的历史记录！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. 存储当前要编辑的记录（供 Popup 按钮使用）
            _currentEditingItem = selectedItemToEdit;

            // 3. 填充 Popup 中所有字段的数据（一一对应）
            // （原有填充逻辑不变，此处省略，保留你之前的代码）
            txtPopupCustomName.Text = selectedItemToEdit.CustomName;
            txtPopupEndpoint.Text = selectedItemToEdit.Endpoint;
            if (int.TryParse(selectedItemToEdit.MachineCount, out int machineCount))
            {
                txtPopupMachineCount.Value = machineCount;
            }
            else
            {
                txtPopupMachineCount.Value = 1;
            }
            foreach (ComboBoxItem item in cboPopuTestMode.Items)
            {
                if (item.Tag.ToString() == selectedItemToEdit.TestMode)
                {
                    cboPopuTestMode.SelectedItem = item;
                    break;
                }
            }
            if (int.TryParse(selectedItemToEdit.PollInterval, out int pollInterval))
            {
                txtPopuPollInterval.Value = pollInterval;
            }
            else
            {
                txtPopuPollInterval.Value = 1;
            }
            txtPopuParametersJson.Text = selectedItemToEdit.ParametersJson;

            // 4. 重置 Popup 偏移量为 0，确保每次打开都在主窗体正中间
            editHistoryPopup.HorizontalOffset = 0;
            editHistoryPopup.VerticalOffset = 0;

            // 5. 显示 Popup 编辑面板（恢复 StaysOpen 为 false，支持失去焦点隐藏）
            editHistoryPopup.StaysOpen = false;
            editHistoryPopup.IsOpen = true;
        }

        /// <summary>
        /// Popup 确定按钮：保存所有字段的编辑内容，更新历史记录
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnPopupConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 判断当前编辑项是否有效
                if (_currentEditingItem == null)
                {
                    MessageBox.Show("编辑失败，未获取到要修改的记录！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    editHistoryPopup.IsOpen = false;
                    return;
                }

                // 2. 从 Popup 控件中获取所有修改后的值
                // 2.1 自定义名称
                string newCustomName = txtPopupCustomName.Text.Trim();

                // 2.2 接口地址
                string newEndpoint = txtPopupEndpoint.Text.Trim();

                // 2.3 机器数量（NumberInput 取值，转换为字符串）
                double newMachineCount = txtPopupMachineCount.Value ?? 1;
                string newMachineCountStr = newMachineCount.ToString();

                // 2.4 测试模式（ComboBox 获取选中项的 Tag）
                string newTestMode = string.Empty;
                if (cboPopuTestMode.SelectedItem is ComboBoxItem selectedTestModeItem)
                {
                    newTestMode = selectedTestModeItem.Tag.ToString();
                }

                // 2.5 频率设置（NumberInput 取值，转换为字符串）
                double newPollInterval = txtPopuPollInterval.Value ?? 1;
                string newPollIntervalStr = newPollInterval.ToString();

                // 2.6 JSON 参数
                string newParametersJson = txtPopuParametersJson.Text.Trim();

                // 3. 读取历史文件
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
                HistoryRoot historyRoot = new HistoryRoot();

                if (File.Exists(historyPath))
                {
                    string existingContent = ReadFileContentWithRetry(historyPath);
                    if (!string.IsNullOrWhiteSpace(existingContent))
                    {
                        historyRoot = JsonConvert.DeserializeObject<HistoryRoot>(existingContent) ?? new HistoryRoot();
                    }
                }

                // 4. 通过 Guid 查找并更新记录
                HistoryItem itemToUpdate = historyRoot.HistoryList.FirstOrDefault(item => item.Id == _currentEditingItem.Id);
                if (itemToUpdate == null)
                {
                    AddLogEntry($"[系统] 编辑失败：未在历史文件中找到该记录（Id：{_currentEditingItem.Id}）", 0, true);
                    MessageBox.Show("编辑失败，未在历史文件中找到该条记录！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    editHistoryPopup.IsOpen = false;
                    return;
                }

                // 5. 更新所有字段的值（覆盖原有数据）
                itemToUpdate.CustomName = newCustomName;
                itemToUpdate.Endpoint = newEndpoint;
                itemToUpdate.MachineCount = newMachineCountStr;
                itemToUpdate.TestMode = newTestMode;
                itemToUpdate.PollInterval = newPollIntervalStr;
                itemToUpdate.ParametersJson = newParametersJson;
                // 可选：更新最后编辑时间（如果你的 HistoryItem 新增了 LastEditTime 字段）
                // itemToUpdate.LastEditTime = DateTime.Now;

                // 6. 覆盖写入历史文件
                string updatedHistoryContent = JsonConvert.SerializeObject(historyRoot, Formatting.Indented);
                using (var fileStream = new FileStream(historyPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var streamWriter = new StreamWriter(fileStream, Encoding.UTF8))
                {
                    streamWriter.Write(updatedHistoryContent);
                    streamWriter.Flush();
                }

                // 7. 刷新 ListBox 并隐藏 Popup
                Dispatcher.Invoke(() =>
                {
                    var sortedUpdatedList = historyRoot.HistoryList
                        .OrderByDescending(item => item.SavedTime)
                        .ToList();

                    lbHistory.ItemsSource = null;
                    lbHistory.ItemsSource = sortedUpdatedList;

                    // 保持选中该记录，让用户看到修改后的结果
                    lbHistory.SelectedItem = itemToUpdate;
                });

                // 8. 提示成功并隐藏 Popup
                AddLogEntry($"[系统] 已成功编辑历史记录（Id：{_currentEditingItem.Id}）", 0);
                MessageBox.Show("所有字段已编辑成功并保存！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                editHistoryPopup.IsOpen = false;
                editHistoryPopup.StaysOpen = false;
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 编辑选中历史记录失败：{ex.Message}", 0, true);
                MessageBox.Show($"编辑失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                editHistoryPopup.IsOpen = false;
            }
        }
        /// <summary>
        /// Popup 取消按钮：放弃编辑，隐藏 Popup
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnPopupCancel_Click(object sender, RoutedEventArgs e)
        {
            // 隐藏 Popup，清空临时变量，避免残留数据影响下次编辑
            editHistoryPopup.IsOpen = false;
            _currentEditingItem = null;
            AddLogEntry("[系统] 用户取消编辑历史记录", 0);
            editHistoryPopup.StaysOpen = false; // 恢复默认状态

        }

        /// <summary>
        /// Popup 拖动功能：无事件绑定，无报错，通过 Dispatcher 实现拖动
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditHistoryPopup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. 确保 Popup 处于打开状态
            if (!editHistoryPopup.IsOpen)
            {
                return;
            }

            // 2. 记录当前鼠标位置和 Popup 偏移量（局部变量）
            Point mouseStartPos = e.GetPosition(this); // 相对于主窗体的鼠标起始位置
            double popupStartHorizontalOffset = editHistoryPopup.HorizontalOffset;
            double popupStartVerticalOffset = editHistoryPopup.VerticalOffset;

            // 3. 捕获鼠标，开启拖动模式
            bool isDragging = true;
            Mouse.Capture(editHistoryPopup);

            // 4. 通过 Dispatcher 循环跟踪鼠标移动，实现拖动（无事件绑定）
            Action dragLoop = null;
            dragLoop = () =>
            {
                if (!isDragging)
                {
                    return;
                }

                // 5. 判断鼠标左键是否保持按下状态
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                {
                    // 计算当前鼠标位置，更新 Popup 偏移量
                    Point mouseCurrentPos = Mouse.GetPosition(this);
                    editHistoryPopup.HorizontalOffset = popupStartHorizontalOffset + (mouseCurrentPos.X - mouseStartPos.X);
                    editHistoryPopup.VerticalOffset = popupStartVerticalOffset + (mouseCurrentPos.Y - mouseStartPos.Y);

                    // 6. 继续循环跟踪鼠标移动（保持拖动）
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, dragLoop);
                }
                else
                {
                    // 7. 鼠标左键松开，结束拖动
                    isDragging = false;
                    Mouse.Capture(null); // 释放鼠标捕获
                    editHistoryPopup.StaysOpen = false; // 恢复失去焦点隐藏状态
                }
            };

            // 8. 启动拖动循环
            Dispatcher.BeginInvoke(DispatcherPriority.Input, dragLoop);

            // 9. 点击 Popup 时，保持置顶
            editHistoryPopup.StaysOpen = true;
            e.Handled = true;
        }

        private async void btnSave_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                SaveHistory();
            });
        }

        private void btnClean_Click(object sender, RoutedEventArgs e)
        {
            ClearMainFormInputs();

        }
        /// <summary>
        /// 公共方法：清空主界面所有输入控件的值（无默认选择，纯清空）
        /// 可被「清空按钮」「重新加载历史」等场景复用
        /// </summary>
        private void ClearMainFormInputs()
        {
            // 2. 接口地址输入框（你的原有控件）
            txtEndpoint.Text = string.Empty;

            // 3. 机器数量 NumberInput（重置为默认值空，而非1，无默认选择）
            txtMachineCount.Value = 1; // 清空值，不默认填充1，保持输入框水印提示

            cboTestMode.SelectedIndex = -1; //
            cboTestMode.Text = string.Empty; // 

            // 5. 频率设置 NumberInput（重置为默认值空，无默认选择）
            txtPollInterval.Value = 1;

            // 6. JSON 参数输入框
            txtParametersJson.Text = string.Empty;
        }


        #region 硬件轮询（独立隔离，无冲突）
        /// <summary>
        /// 启动硬件信息轮询（独立变量，不和业务轮询冲突）
        /// </summary>
        /// <param name="pollIntervalMs">轮询间隔（毫秒，默认2000ms=2秒，避免过频繁）</param>
        private async Task StartHardwarePolling(int pollIntervalMs = 2000,int full=0)
        {
            // 避免重复启动硬件轮询
            if (_isHardwarePolling)
            {
                AddLogEntry("[系统] 硬件统计轮询已在运行中，无需重复启动！", 0, false);
                return;
            }

            // 初始化硬件轮询专属Cts（和业务轮询隔离，无冲突）
            _hardwarePollingCts = new CancellationTokenSource();
            _isHardwarePolling = true;
            CancellationToken token = _hardwarePollingCts.Token;

            try
            {
                AddLogEntry($"[系统] 硬件统计轮询已启动，间隔：{pollIntervalMs / 1000}秒", 0, false, FixedLogType.HardwareMonitor);

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // ===== 关键修改：彻底移除值元组，分步获取硬件信息（无编译错误） =====
                        string hardwareLogInfo = string.Empty;
                        HardwareMonitorHelper.HardwarePanelStatus hardwarePanelStatus = null;

                        // 异步在后台线程获取硬件信息（不阻塞UI，兼容 .NET Framework 4.6）
                        await Task.Run(() =>
                        {
                            // 1. 获取用于日志输出的完整硬件信息 显示整个硬件信息，可用，显示太占日志空间，不打开
                            if (full > 0)
                            {
                                hardwareLogInfo = HardwareMonitorHelper.GetFullHardwareStatisticInfo();
                            }
                            // 2. 获取用于UI面板的格式化数据（一键返回，无需解构）
                            hardwarePanelStatus = HardwareMonitorHelper.GetFullHardwarePanelStatus();
                        }, token);

                        #region 显示整个硬件信息，可用，显示太占日志空间，不打开
                        if (full > 0)
                        {
                            // ===== 2. 更新日志（异步非阻塞，后台优先级，无值元组） =====
                            _ = Dispatcher.BeginInvoke(
                                DispatcherPriority.Background,
                                new Action(() =>
                                {
                                    AddLogEntry(hardwareLogInfo, 0, false);
                                    // 可选：如果面板解析失败，输出错误日志
                                    if (hardwarePanelStatus != null && hardwarePanelStatus.IsError)
                                    {
                                        AddLogEntry($"[硬件面板] 数据解析失败：{hardwarePanelStatus.ErrorMessage}", 0, true);
                                    }
                                })
                            );
                        }

                        #endregion
                        //AddLogEntry("[监控] CPU使用情况："+hardwarePanelStatus.CpuStatusText + "内存使用情况："+hardwarePanelStatus.MemoryStatusText, 0, false);
                        // ===== 关键修改：两句独立日志（CPU 单独一句、内存 单独一句） =====
                        _ = Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() =>
                            {
                                // 1. 输出完整硬件详细日志（可选保留，如需精简可删除）
                                //AddLogEntry(hardwareLogInfo, 0, false);

                                // 初始化默认日志内容（容错备用）
                                string cpuLog = "[监控] CPU 占用：0.0% 总量：0核/0线程";
                                string memoryLog = "[监控] 内存 占用：0.0% 总量：0.0GB";

                                if (hardwarePanelStatus != null)
                                {
                                    // ===== 第一句：CPU 日志（独立输出，格式：占用：xxx 总量：xxx） =====
                                    string cpuStatus = hardwarePanelStatus.CpuStatusText ?? "0.0%/0核/0线程";
                                    string[] cpuParts = cpuStatus.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    string cpuUsage = "0.0%";
                                    string cpuTotal = "0核/0线程";

                                    if (cpuParts.Length >= 1) cpuUsage = cpuParts[0];
                                    if (cpuParts.Length >= 3) cpuTotal = $"{cpuParts[1]}/{cpuParts[2]}";
                                    // 构建 CPU 独立日志
                                    cpuLog = $"[监控] CPU 占用：{cpuUsage} 总量：{cpuTotal}";

                                    // ===== 第二句：内存 日志（独立输出，格式：占用：xxx 总量：xxx） =====
                                    string memoryStatus = hardwarePanelStatus.MemoryStatusText ?? "0.0%/0.0GB/0.0GB";
                                    string[] memoryParts = memoryStatus.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    string memoryUsage = "0.0%";
                                    string memoryTotal = "0.0GB";
                                    string memoryUsage2 = "0.0GB";

                                    if (memoryParts.Length >= 1) memoryUsage = memoryParts[0];
                                    if (memoryParts.Length >= 2) memoryUsage2 = memoryParts[1];
                                    if (memoryParts.Length >= 3) memoryTotal = memoryParts[2];
                                    // 构建 内存 独立日志
                                    memoryLog = $"[监控] 内存 占用：{memoryUsage} 可用：{memoryUsage2} 总量：{memoryTotal}";
                                }

                                // 2. 输出两句独立监控日志（核心需求：CPU 和 内存单独输出）
                                AddLogEntry(cpuLog, 0, false, FixedLogType.HardwareMonitor);
                                AddLogEntry(memoryLog, 0, false, FixedLogType.HardwareMonitor);

                                // 3. 面板解析失败日志（原有容错逻辑，保留）
                                if (hardwarePanelStatus != null && hardwarePanelStatus.IsError)
                                {
                                    AddLogEntry($"[硬件面板] 数据解析失败：{hardwarePanelStatus.ErrorMessage}", 0, true);
                                }
                            })
                        );

                        // ===== 3. 更新UI面板（异步非阻塞，正常优先级，保证实时性） =====
                        _ = Dispatcher.BeginInvoke(
                            DispatcherPriority.Normal,
                            new Action(() =>
                            {
                                // 容错：避免硬件面板数据为null导致空引用异常
                                if (hardwarePanelStatus != null)
                                {
                                    txtCPU.Text = hardwarePanelStatus.CpuStatusText;
                                    txtMemory.Text = hardwarePanelStatus.MemoryStatusText;
                                }
                                else
                                {
                                    txtCPU.Text = "0.0%/0核/0线程";
                                    txtMemory.Text = "0.0%/0.0GB/0.0GB";
                                }
                            })
                        );

                        // ===== 4. 轮询间隔等待（异步，不阻塞任何线程，兼容 .NET Framework 4.6） =====
                        await Task.Delay(pollIntervalMs, token);
                    }
                    catch (Exception ex)
                    {
                        // 异常处理：日志输出，延迟重试
                        _ = Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() =>
                            {
                                AddLogEntry($"[硬件监控] 单次查询/解析失败：{ex.Message}", 0, true);
                            })
                        );

                        // 出错后延迟1秒重试，避免频繁报错积压
                        await Task.Delay(1000, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // 正常取消：轮询被主动停止（如关闭程序）
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        AddLogEntry($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 硬件统计轮询已正常停止！", 0, false);
                    })
                );
            }
            catch (Exception ex)
            {
                // 致命异常：轮询整体崩溃
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        AddLogEntry($"[系统异常] 硬件统计轮询整体异常：{ex.Message}", 0, true);
                    })
                );
            }
            finally
            {
                // 重置状态 + 释放资源（避免内存泄漏，兼容 .NET Framework 4.6）
                _isHardwarePolling = false;
                if (_hardwarePollingCts != null)
                {
                    _hardwarePollingCts.Dispose();
                    _hardwarePollingCts = null;
                }

                // 可选：停止轮询后重置面板显示
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(() =>
                    {
                        txtCPU.Text = "0.0%/0核/0线程";
                        txtMemory.Text = "0.0GB/0.0GB/0.0GB";
                    })
                );
            }
        }

        /// <summary>
        /// 停止硬件信息轮询（只操作专属Cts，不影响业务轮询）
        /// </summary>
        private void StopHardwarePolling()
        {
            if (_hardwarePollingCts != null && !_hardwarePollingCts.IsCancellationRequested)
            {
                _hardwarePollingCts.Cancel();
                _hardwarePollingCts.Dispose(); // 释放资源，避免内存泄漏
                _hardwarePollingCts = null;
            }
        }
        #endregion

        /// <summary>
        /// 硬件监控日志过滤按钮点击事件
        /// </summary>
        private void BtnHardwareLogs_Click(object sender, RoutedEventArgs e)
        {
            // 1. 更新当前选中的固定日志类型
            _currentSelectedFixedLogType = FixedLogType.HardwareMonitor;

            // 2. 更新按钮样式（高亮当前，重置其他）
            ResetFilterButtonStyles();
            btnHardwareLogs.Background = (SolidColorBrush)FindResource("PrimaryColor");

            // 3. 强制刷新日志UI（应用过滤）
            UpdateGlobalLogUI();
            StatsUpdateTimer_Tick(null, null);

            AddLogEntry("[系统] 已切换到「硬件监控日志」过滤", 0, false, FixedLogType.System);
        }

        /// <summary>
        /// 系统日志过滤按钮点击事件
        /// </summary>
        private void BtnSystemLogs_Click(object sender, RoutedEventArgs e)
        {
            // 1. 更新当前选中的固定日志类型
            _currentSelectedFixedLogType = FixedLogType.System;

            // 2. 更新按钮样式（高亮当前，重置其他）
            ResetFilterButtonStyles();
            btnSystemLogs.Background = (SolidColorBrush)FindResource("PrimaryColor");

            // 3. 强制刷新日志UI（应用过滤）
            UpdateGlobalLogUI();
            StatsUpdateTimer_Tick(null, null);

            AddLogEntry("[系统] 已切换到「系统日志」过滤", 0, false, FixedLogType.System);
        }
    }
}