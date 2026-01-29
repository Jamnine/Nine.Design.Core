using Newtonsoft.Json;
using Panuon.WPF.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Media;
using System.Windows.Threading;

namespace Nine.Design.PollingTool
{
    /// <summary>
    /// 主窗口（完整功能，兼容C# 7.3 / .NET Framework 4.6）
    /// </summary>
    public partial class MainWindow : WindowX
    {
        // 基础配置
        private HttpMethod _selectedHttpMethod = HttpMethod.Post;
        private CancellationTokenSource _cts;
        private List<Task> _pollingTasks = new List<Task>();
        private HttpClient _httpClient;
        private bool _isPolling = false;
        private readonly ObservableCollection<MachineUserControl> _machines = new ObservableCollection<MachineUserControl>();
        private SynchronizationContext _uiContext;

        // 日志配置
        private readonly List<Brush> _logBrushes = new List<Brush>
        {
            Brushes.Black, Brushes.DarkBlue, Brushes.DarkGreen, Brushes.DarkRed,
            Brushes.DarkOrange, Brushes.Purple, Brushes.Teal, Brushes.Maroon
        };
        private readonly Dictionary<int, Dictionary<string, int>> _errorStats = new Dictionary<int, Dictionary<string, int>>();
        private int _requestTimeoutSeconds = 10;
        private string _tempLogFilePath;
        private string _logDirectory;
        private readonly object _logLock = new object();
        private bool _isLogInitialized = false;
        private const int FileOperationRetryCount = 3;
        private const int FileOperationRetryDelay = 100;

        // 高频请求控制
        private int _maxRequestsPerSecond = 200;
        private readonly Dictionary<int, DateTime> _machineLastRequestTime = new Dictionary<int, DateTime>();
        private readonly ConcurrentQueue<string> _logCache = new ConcurrentQueue<string>();
        private DispatcherTimer _logUiTimer;

        // 端口监控与统计
        private int _maxPortUsage = 50000;
        private DispatcherTimer _portMonitorTimer;
        private int _currentPortCount = 0;
        private int _totalRequestsSent = 0;
        private int _totalSuccessfulRequests = 0;
        private readonly object _requestCountLock = new object();
        private DateTime _pollingStartTime;
        private DispatcherTimer _statsUpdateTimer; // 统计更新定时器

        // 响应时间极值
        private readonly Dictionary<int, Tuple<double, double>> _responseTimeExtremes = new Dictionary<int, Tuple<double, double>>();

        // 多机器统计：线程安全的机器维度计数器（核心修复）
        private readonly ConcurrentDictionary<int, int> _machineTotalRequests = new ConcurrentDictionary<int, int>();
        private readonly ConcurrentDictionary<int, int> _machineSuccessRequests = new ConcurrentDictionary<int, int>();

        // 总轮询时长（修复未定义变量）
        private TimeSpan _totalPollingTime = TimeSpan.Zero;

        // 构造函数（WPF自动生成InitializeComponent，需确保XAML文件存在）
        public MainWindow()
        {
            InitializeComponent();
            // 设置基准字体大小
            this.FontSize = 10;
            // 初始化核心组件
            InitializeTcpParameters();
            RegisterGlobalExceptionHandlers();
            InitializeLogSystem();
            InitializeLogUiTimer();
            InitializeHttpClient();
            InitializePortMonitorTimer();
            InitializeStatsTimer(); // 初始化统计更新定时器

            Loaded += (s, e) =>
            {
                _uiContext = SynchronizationContext.Current;
                rbGet.Checked += HttpMethodChecked;
                rbPost.Checked += HttpMethodChecked;
                TxtEndpoint_TextChanged(null, null);
                txtEndpoint.TextChanged += TxtEndpoint_TextChanged;
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
                _cts = new CancellationTokenSource();
                UpdateMachineList();
                txtMachineCount.TextChanged += TxtMachineCount_TextChanged;

                // 加载历史记录
                LoadHistory();

                // 初始化日志
                AddLogEntry("[系统] 程序启动，实时统计面板已启用", 0);
                AddLogEntry($"[系统] 默认请求频率：{_maxRequestsPerSecond}次/秒 | 端口上限：{_maxPortUsage}", 0);
            };

            Closing += MainWindow_Closing;
        }

        // 窗口大小改变时调整字体大小
        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 根据窗口宽度计算缩放比例
            double scale = Math.Max(0.8, Math.Min(1.5, e.NewSize.Width / 1400));
            this.FontSize = 12 * scale;
        }

        #region 初始化方法
        // 初始化TCP参数（端口复用）
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
                    // .NET Framework 4.6 支持ReusePort
                    ServicePointManager.ReusePort = true;
                    AddLogEntry("[系统] TCP端口重用已启用", 0);
                }

                // 尝试修改注册表（管理员权限）
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

        // 初始化HttpClient（兼容.NET Framework 4.6，移除Core专属属性）
        private void InitializeHttpClient()
        {
            try
            {
                // .NET Framework 4.6 兼容的 HttpClientHandler 配置（移除不存在的属性）
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = false,
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                // 关键：.NET Framework 4.6 中忽略 SSL 证书验证（全局配置，替代 ServerCertificateCustomValidationCallback）
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

                // 初始化 HttpClient（连接池大小已通过 ServicePointManager.DefaultConnectionLimit 全局配置）
                _httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds)
                };

                AddLogEntry("[系统] HttpClient初始化完成（连接池大小：1000，已启用SSL证书忽略）", 0);
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] HttpClient初始化失败：{ex.Message}", 0, true);
                // 异常时备用初始化 HttpClient
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds)
                };
                // 备用初始化也配置SSL忽略
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            }
        }

        // 初始化端口监控定时器
        private void InitializePortMonitorTimer()
        {
            _portMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _portMonitorTimer.Tick += PortMonitorTimer_Tick;
            _portMonitorTimer.Start();
            AddLogEntry("[系统] 端口监控定时器已启动（5秒/次）", 0);
        }

        // 初始化统计更新定时器（每秒更新UI）
        private void InitializeStatsTimer()
        {
            _statsUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsUpdateTimer.Tick += StatsUpdateTimer_Tick;
            _statsUpdateTimer.Start();
            AddLogEntry("[系统] 实时统计定时器已启动（1秒/次）", 0);
        }

        // 初始化日志系统
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

        // 初始化日志UI更新定时器
        private void InitializeLogUiTimer()
        {
            _logUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _logUiTimer.Tick += LogUiTimer_Tick;
            _logUiTimer.Start();
        }

        // 注册全局异常处理
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

        #region 定时器回调
        // 端口监控定时器
        private void PortMonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _currentPortCount = GetCurrentProcessTcpConnectionCount();
                AddLogEntry($"[监控] 端口占用：{_currentPortCount}/{_maxPortUsage} | 总请求：{_totalRequestsSent} | 成功：{_totalSuccessfulRequests}", 0);

                if (_currentPortCount > _maxPortUsage * 0.8)
                {
                    AddLogEntry($"[警告] 端口占用率已达80%（{_currentPortCount}/{_maxPortUsage}）", 0, true);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[监控] 端口统计失败：{ex.Message}", 0);
            }
        }

        // 统计更新定时器（实时更新UI）
        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 更新总请求数
                    txtTotalRequests.Text = _totalRequestsSent.ToString();

                    // 更新成功请求数
                    txtSuccessRequests.Text = _totalSuccessfulRequests.ToString();

                    // 计算成功率
                    double successRate = _totalRequestsSent > 0
                        ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100
                        : 0;
                    txtSuccessRate.Text = $"{successRate:F2}%";

                    // 更新端口占用
                    txtPortUsage.Text = $"{_currentPortCount}/{_maxPortUsage}";

                    // 更新运行时长
                    if (_isPolling && _pollingStartTime != DateTime.MinValue)
                    {
                        TimeSpan runTime = DateTime.Now - _pollingStartTime;
                        txtRunTime.Text = $"{runTime.Hours:D2}:{runTime.Minutes:D2}:{runTime.Seconds:D2}";
                    }
                    else
                    {
                        txtRunTime.Text = "00:00:00";
                    }
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[统计] UI更新失败：{ex.Message}", 0);
            }
        }

        // 日志UI批量更新
        private void LogUiTimer_Tick(object sender, EventArgs e)
        {
            if (_logCache.IsEmpty) return;

            List<string> logsToAdd = new List<string>();
            int maxBatchSize = 100;
            int count = 0;

            while (_logCache.TryDequeue(out string log) && count < maxBatchSize)
            {
                logsToAdd.Add(log);
                count++;
            }

            foreach (var log in logsToAdd)
            {
                bool isError = log.Contains("错误:") || log.Contains("警告:");
                int machineId = 0;

                if (log.Contains("[机器"))
                {
                    try
                    {
                        var machinePart = log.Split(new[] { "[机器 " }, StringSplitOptions.None)[1];
                        machineId = int.Parse(machinePart.Split(']')[0].Trim());
                    }
                    catch { }
                }

                ListBoxItem logItem = new ListBoxItem
                {
                    Content = log,
                    Padding = new Thickness(2)
                };

                if (isError)
                {
                    logItem.Foreground = Brushes.Red;
                    logItem.FontWeight = FontWeights.Bold;
                }
                else if (machineId > 0)
                {
                    int colorIndex = (machineId - 1) % _logBrushes.Count;
                    logItem.Foreground = _logBrushes[colorIndex];
                }
                else
                {
                    logItem.Foreground = Brushes.DarkGray;
                }

                // 限制日志数量，避免内存溢出
                if (lbLogs.Items.Count > 50000)
                {
                    for (int i = 0; i < 1000 && lbLogs.Items.Count > 0; i++)
                    {
                        lbLogs.Items.RemoveAt(0);
                    }
                }

                lbLogs.Items.Add(logItem);
            }

            if (logsToAdd.Count > 0 && lbLogs.Items.Count > 0)
            {
                lbLogs.ScrollIntoView(lbLogs.Items[lbLogs.Items.Count - 1]);
            }
        }
        #endregion

        #region 核心业务逻辑
        // 切换请求方法
        private void HttpMethodChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                _selectedHttpMethod = radioButton == rbGet ? HttpMethod.Get : HttpMethod.Post;
                AddLogEntry($"[系统] 请求方法已切换为: {_selectedHttpMethod.Method}", 0);
            }
        }

        // 更新机器列表
        private void UpdateMachineList()
        {
            // 确保在 UI 线程执行（Dispatcher.Invoke 同步执行，适配 WPF 控件操作，避免跨线程修改集合）
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 前置校验1：确保 _machines 集合已初始化（避免空引用异常，核心容错）
                    if (_machines == null)
                    {
                        AddLogEntry("[系统] 错误：机器列表集合未初始化，无法更新列表", 0, true);
                        return;
                    }

                    // 前置校验2：确保 txtMachineCount 控件已加载（避免控件空引用）
                    if (txtMachineCount == null)
                    {
                        AddLogEntry("[系统] 错误：机器数量输入框未找到，无法更新列表", 0, true);
                        return;
                    }

                    // 步骤1：获取并校验机器数量输入值
                    string machineCountText = txtMachineCount.Text.Trim(); // 去除前后空格，提升容错性
                    int machineCount;
                    bool isMachineCountValid = int.TryParse(machineCountText, out machineCount) && machineCount > 0;

                    if (isMachineCountValid)
                    {
                        // 优化：当前数量与输入数量一致，直接返回，避免无效的清空/重建操作
                        if (_machines.Count == machineCount)
                        {
                            return;
                        }

                        // 步骤2：清空现有机器列表（安全操作 ObservableCollection，UI 线程内无异常）
                        _machines.Clear();

                        // 步骤3：重置所有统计数据（全局统计 + 机器维度统计，避免历史数据干扰）
                        ResetAllStats();

                        // 步骤4：循环创建机器实例，初始化相关配置
                        for (int i = 1; i <= machineCount; i++)
                        {
                            // 实例化机器用户控件，设置核心属性
                            MachineUserControl machineCtrl = new MachineUserControl
                            {
                                MachineId = i,
                                IsPolling = false // 初始化为非轮询状态，保持状态一致性
                            };
                            _machines.Add(machineCtrl);

                            // 步骤5：初始化机器最后请求时间（不存在则添加，避免键不存在异常）
                            if (!_machineLastRequestTime.ContainsKey(i))
                            {
                                _machineLastRequestTime.Add(i, DateTime.MinValue); // 替代索引赋值，更安全
                            }
                            else
                            {
                                // 重置已有机器的最后请求时间，保持数据干净
                                _machineLastRequestTime[i] = DateTime.MinValue;
                            }

                            // 步骤6：初始化机器维度的统计计数器（线程安全，TryAdd 避免重复添加报错）
                            _machineTotalRequests.TryAdd(i, 0);
                            _machineSuccessRequests.TryAdd(i, 0);

                            // 额外：若计数器已存在，重置为 0（避免历史统计残留）
                            if (_machineTotalRequests.ContainsKey(i))
                            {
                                _machineTotalRequests[i] = 0;
                            }
                            if (_machineSuccessRequests.ContainsKey(i))
                            {
                                _machineSuccessRequests[i] = 0;
                            }
                        }

                        // 步骤7：更新 ItemsControl 数据源（优化写法，避免重复赋值 null，减少 UI 无效刷新）
                        if (icMachines != null) // 校验 icMachines 控件是否存在
                        {
                            if (icMachines.ItemsSource != _machines)
                            {
                                icMachines.ItemsSource = _machines;
                            }
                        }
                        else
                        {
                            AddLogEntry("[系统] 警告：机器列表控件 icMachines 未找到，列表无法渲染", 0, true);
                        }

                        // 步骤8：记录正常更新日志，反馈当前列表数量
                        AddLogEntry($"[系统] 机器列表更新成功，当前机器数量: {_machines.Count}", 0);
                    }
                    else
                    {
                        // 无效机器数量：记录提示日志，引导用户输入正确格式
                        string errorMsg = string.IsNullOrEmpty(machineCountText)
                            ? "机器数量不能为空，请输入正整数"
                            : $"无效的机器数量「{machineCountText}」，请输入大于 0 的整数";
                        AddLogEntry($"[系统] {errorMsg}", 0);
                    }
                }
                catch (Exception ex)
                {
                    // 捕获所有未预期异常，避免程序崩溃，同时记录详细错误信息便于排查
                    AddLogEntry($"[系统] 更新机器列表失败：{ex.Message}，异常堆栈：{ex.StackTrace}", 0, true);
                }
            });
        }

        // 重置所有统计（全局+机器维度）
        private void ResetAllStats()
        {
            // 仅当机器数量变化时，才重置全局统计（可选优化）
            lock (_requestCountLock)
            {
                _totalRequestsSent = 0;
                _totalSuccessfulRequests = 0;
            }
            _machineTotalRequests.Clear();
            _machineSuccessRequests.Clear();
            ResetErrorStats();
            ResetResponseTimeExtremes();
        }

        // 重置错误统计
        private void ResetErrorStats()
        {
            _errorStats.Clear();
            foreach (var machine in _machines)
            {
                _errorStats[machine.MachineId] = new Dictionary<string, int>
                {
                    { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                    { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                    { "端口超限", 0 }, { "频率限制", 0 }
                };
            }
        }

        // 重置响应时间极值
        private void ResetResponseTimeExtremes()
        {
            _responseTimeExtremes.Clear();
            foreach (var machine in _machines)
            {
                _responseTimeExtremes[machine.MachineId] = new Tuple<double, double>(double.MaxValue, double.MinValue);
            }
        }

        // 清空日志方法（修复ConcurrentQueue.Clear()不兼容）
        private void ClearLogs()
        {
            Dispatcher.Invoke(() =>
            {
                lbLogs.Items.Clear();
            });

            // .NET Framework 4.6 中ConcurrentQueue无Clear()，通过循环Dequeue清空
            while (_logCache.TryDequeue(out _)) ;

            AddLogEntry("[系统] 日志已清空", 0);
        }

        // 核心修复：计数方法（1次请求只统计1次）
        private void IncrementRequestCount(int machineId, bool isSuccess = false)
        {
            lock (_requestCountLock)
            {
                // 总请求数只加1次（核心修复：避免重复累加）
                _totalRequestsSent++;
                _machineTotalRequests.AddOrUpdate(machineId, 1, (key, oldValue) => oldValue + 1);

                // 只有成功时才加成功数
                if (isSuccess)
                {
                    _totalSuccessfulRequests++;
                    _machineSuccessRequests.AddOrUpdate(machineId, 1, (key, oldValue) => oldValue + 1);
                }
            }
        }

        // 核心轮询方法（修复重复计数，移除不兼容的ReadAsStringAsync(cancellationToken)）
        private async Task PollEndpoint(int machineId, string endpoint, HttpMethod requestMethod, CancellationToken cancellationToken)
        {
            AddLogEntry($"轮询任务已启动（高频模式）", machineId);
            var machineStats = _errorStats[machineId];
            int maxRetryCount = 3;

            while (!cancellationToken.IsCancellationRequested && _isPolling)
            {
                // 端口超限检查
                if (IsPortUsageExceeded())
                {
                    lock (machineStats) machineStats["端口超限"]++;
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

                        // 频率控制
                        TimeSpan delayTime = TimeSpan.Zero;
                        if (_maxRequestsPerSecond > 0)
                        {
                            lock (_machineLastRequestTime)
                            {
                                var lastTime = _machineLastRequestTime[machineId];
                                var minInterval = TimeSpan.FromSeconds(1.0 / _maxRequestsPerSecond);
                                var currentTime = DateTime.Now;
                                if (currentTime - lastTime < minInterval)
                                {
                                    delayTime = minInterval - (currentTime - lastTime);
                                    lock (machineStats) machineStats["频率限制"]++;
                                }
                                _machineLastRequestTime[machineId] = DateTime.Now;
                            }

                            if (delayTime > TimeSpan.Zero)
                            {
                                await Task.Delay(delayTime, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        AddLogEntry($"发起请求...(重试：{retryCount})", machineId);
                        DateTime requestStartTime = DateTime.Now;
                        HttpResponseMessage response = null;
                        bool isRequestProcessed = false;

                        // 读取JSON参数
                        string jsonParameters = null;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            jsonParameters = GetJsonParameters();
                        });

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
                                lock (machineStats) machineStats["参数错误"]++;
                                // 空参数请求：统计1次（仅总请求）
                                IncrementRequestCount(machineId, false);
                                isRequestSuccess = true;
                                break;
                            }
                        }

                        // 处理响应（核心修复：只统计1次，移除不兼容的ReadAsStringAsync带参重载）
                        if (isRequestProcessed)
                        {
                            await Task.Run(async () =>
                            {
                                try
                                {
                                    string result = string.Empty;
                                    bool isSuccess = false;

                                    try
                                    {
                                        // .NET Framework 4.6 只支持无参ReadAsStringAsync()
                                        // 如需取消，依赖外层cancellationToken（HttpClient请求已带取消令牌）
                                        result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        isSuccess = true;

                                        // 更新响应时间极值
                                        TimeSpan responseTime = DateTime.Now - requestStartTime;
                                        double responseTimeMs = responseTime.TotalMilliseconds;
                                        lock (_responseTimeExtremes)
                                        {
                                            var current = _responseTimeExtremes[machineId];
                                            _responseTimeExtremes[machineId] = new Tuple<double, double>(
                                                Math.Min(current.Item1, responseTimeMs),
                                                Math.Max(current.Item2, responseTimeMs));
                                        }

                                        // 记录状态码提示（非2xx时）
                                        if (!response.IsSuccessStatusCode)
                                        {
                                            AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 状态码: {(int)response.StatusCode} | 结果: {result}", machineId);
                                            lock (machineStats) machineStats["HTTP错误"]++;
                                        }
                                        else
                                        {
                                            AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 结果: {result}", machineId);
                                        }

                                        lock (machineStats) machineStats["成功次数"]++;
                                    }
                                    catch (Exception ex)
                                    {
                                        // 响应读取失败
                                        lock (machineStats) machineStats["其他错误"]++;
                                        AddLogEntry($"响应处理错误: {ex.Message}", machineId, true);
                                    }

                                    // 核心修复：只调用1次计数方法
                                    IncrementRequestCount(machineId, isSuccess);

                                }
                                catch (Exception ex)
                                {
                                    // 响应处理异常：统计1次（仅总请求）
                                    IncrementRequestCount(machineId, false);
                                    AddLogEntry($"响应处理异常: {ex.Message}", machineId, true);
                                    lock (machineStats) machineStats["其他错误"]++;
                                }
                            }, cancellationToken).ConfigureAwait(false);
                        }

                        isRequestSuccess = true;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("每个套接字地址") || ex.Message.Contains("only use once"))
                    {
                        retryCount++;
                        lock (machineStats) machineStats["网络错误"]++;
                        AddLogEntry($"错误: 端口耗尽 - {ex.Message} (重试 {retryCount}/{maxRetryCount})", machineId, true);
                        // 端口耗尽：统计1次（仅总请求）
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
                            lock (machineStats) machineStats["超时"]++;
                            AddLogEntry($"错误: 请求超时（{_requestTimeoutSeconds}秒）", machineId, true);
                            // 超时：统计1次（仅总请求）
                            IncrementRequestCount(machineId, false);
                        }
                        isRequestSuccess = true;
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        lock (machineStats) machineStats["网络错误"]++;
                        AddLogEntry($"错误: 网络异常 - {ex.Message}", machineId, true);
                        // 网络错误：统计1次（仅总请求）
                        IncrementRequestCount(machineId, false);
                        retryCount = maxRetryCount;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lock (machineStats) machineStats["其他错误"]++;
                        AddLogEntry($"错误: {ex.Message}", machineId, true);
                        // 其他错误：统计1次（仅总请求）
                        IncrementRequestCount(machineId, false);
                        retryCount = maxRetryCount;
                        break;
                    }
                }
            }

            AddLogEntry($"轮询任务已停止", machineId);
        }

        // 开始轮询
        private async void StartPolling_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // 更新频率限制
                if (int.TryParse(txtPollInterval.Text, out int userMaxRequests) && userMaxRequests > 0)
                {
                    _maxRequestsPerSecond = userMaxRequests;
                    AddLogEntry($"[系统] 请求频率更新为：{_maxRequestsPerSecond}次/秒", 0);
                }

                UpdateMachineList();
                await StartPollingAsync();
                SaveHistory();
            });
        }

        // 开始轮询异步方法
        private async Task StartPollingAsync()
        {
            try
            {
                // 步骤1：验证参数（内部不再定义machineCount，避免作用域冲突）
                await Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrWhiteSpace(txtEndpoint.Text))
                    {
                        AddLogEntry("[系统] 错误: Endpoint不能为空", 0, true);
                        MessageBox.Show("请输入有效的Endpoint地址", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new InvalidOperationException("Endpoint不能为空");
                    }

                    // 仅做验证，不定义外部变量，避免作用域冲突
                    int tempMachineCount;
                    if (!int.TryParse(txtMachineCount.Text, out tempMachineCount) || tempMachineCount <= 0)
                    {
                        AddLogEntry("[系统] 机器数量无效", 0);
                        MessageBox.Show("机器数量必须是大于0的整数", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new InvalidOperationException("机器数量无效");
                    }

                    AddLogEntry($"[系统] 频率限制: {_maxRequestsPerSecond}次/秒 | 端口上限: {_maxPortUsage}", 0);
                });

                // 步骤2：获取参数（单独获取，避免变量名冲突，适配.NET Framework 4.6）
                string endpoint = await Dispatcher.InvokeAsync(() => txtEndpoint.Text);
                HttpMethod requestMethod = _selectedHttpMethod;
                // 修复：显式获取机器数量，避免与内部验证变量冲突
                int machineCount = await Dispatcher.InvokeAsync(() =>
                {
                    int tempCount;
                    int.TryParse(txtMachineCount.Text, out tempCount);
                    return tempCount;
                });

                // 步骤3：初始化轮询参数
                _pollingTasks.Clear();
                _isPolling = true;
                _cts = new CancellationTokenSource();

                _pollingStartTime = DateTime.Now;
                // 重置统计（避免历史数据干扰）
                ResetAllStats();

                // 步骤4：启动多机器轮询
                for (int i = 1; i <= machineCount; i++)
                {
                    int currentMachineId = i;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (currentMachineId <= _machines.Count)
                        {
                            _machines[currentMachineId - 1].IsPolling = true;
                        }
                    });

                    var task = Task.Run(() => PollEndpoint(currentMachineId, endpoint, requestMethod, _cts.Token), _cts.Token);
                    _pollingTasks.Add(task);
                }

                // 步骤5：更新UI状态
                await Dispatcher.InvokeAsync(() =>
                {
                    btnStartPolling.IsEnabled = false;
                    AddLogEntry($"[系统] 高频轮询已开始. 机器数: {machineCount}, 频率: {_maxRequestsPerSecond}次/秒", 0);
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 启动轮询错误: {ex.Message}", 0, true);
                _isPolling = false;
            }
        }

        // 停止轮询
        private async void StopPolling_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await StopPollingAsync();
                OutputErrorStatsSummary();
                ExportLogsToFile();
            });
        }

        // 停止轮询异步方法
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
                        _totalPollingTime = totalTime; // 更新总轮询时长
                        AddLogEntry($"[系统] 轮询总时长: {totalTime:hh\\:mm\\:ss}", 0);
                    }

                    _cts.Dispose();
                    _cts = null;
                }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var machine in _machines)
                {
                    machine.IsPolling = false;
                }

                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
            });

            AddLogEntry("[系统] 高频轮询已停止", 0);
            AddLogEntry($"[统计] 总请求: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {(_totalRequestsSent > 0 ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100 : 0):F2}%", 0);
        }
        #endregion

        #region 辅助方法
        // 获取当前进程TCP连接数
        private int GetCurrentProcessTcpConnectionCount()
        {
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var connections = properties.GetActiveTcpConnections();
                int currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

                return connections.Count(c =>
                    c.LocalEndPoint.Port >= 1024 &&
                    c.LocalEndPoint.Port <= 65534 &&
                    c.State != TcpState.TimeWait);
            }
            catch (Exception ex)
            {
                AddLogEntry($"[监控] 获取端口数失败: {ex.Message}", 0);
                return 0;
            }
        }

        // 检查端口是否超限
        private bool IsPortUsageExceeded()
        {
            return _currentPortCount >= _maxPortUsage;
        }

        // 获取JSON参数
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

        // 输出统计汇总（多机器准确）
        private void OutputErrorStatsSummary()
        {
            Dispatcher.Invoke(() =>
            {
                AddLogEntry("==================================== 统计汇总 ====================================", 0);
                TimeSpan totalTime = _pollingStartTime != DateTime.MinValue ? DateTime.Now - _pollingStartTime : TimeSpan.Zero;
                string timeFormat = $"{totalTime.Hours:D2}:{totalTime.Minutes:D2}:{totalTime.Seconds:D2}.{totalTime.Milliseconds:D3}";

                AddLogEntry($"[全局] 总时长: {timeFormat} | 频率: {_maxRequestsPerSecond}次/秒 | 端口上限: {_maxPortUsage}", 0);
                AddLogEntry($"[全局] 总请求: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {(_totalRequestsSent > 0 ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100 : 0):F2}%", 0);
                AddLogEntry($"[全局] 最终端口占用: {_currentPortCount}/{_maxPortUsage}", 0);

                foreach (var kvp in _errorStats)
                {
                    int machineId = kvp.Key;
                    var stats = kvp.Value;
                    var extremes = _responseTimeExtremes[machineId];

                    // 使用机器维度的实际统计数
                    int machineTotal = _machineTotalRequests.TryGetValue(machineId, out int total) ? total : 0;
                    int machineSuccess = _machineSuccessRequests.TryGetValue(machineId, out int success) ? success : 0;
                    double successRate = machineTotal > 0 ? (double)machineSuccess / machineTotal * 100 : 0;

                    string minTime = machineSuccess > 0 ? $"{extremes.Item1:F2}ms" : "无数据";
                    string maxTime = machineSuccess > 0 ? $"{extremes.Item2:F2}ms" : "无数据";

                    // 展示完整的机器统计
                    string summary = $"机器 {machineId} | 总请求: {machineTotal} | 成功: {machineSuccess} ({successRate:F2}%) | 最短响应: {minTime} | 最长响应: {maxTime} | 超时: {stats["超时"]} | 网络错误: {stats["网络错误"]} | HTTP错误: {stats["HTTP错误"]}";
                    AddLogEntry(summary, 0);
                }
                AddLogEntry("=================================================================================", 0);
            });
        }

        // 清空日志（按钮点击事件）
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            ClearLogs();
        }

        // 处理未捕获异常
        private void HandleUnhandledException(Exception ex, string exceptionType)
        {
            if (ex == null) return;

            string errorMsg = $"[{exceptionType}] 异常: {ex.Message}\n堆栈: {ex.StackTrace}";
            AddLogEntry(errorMsg, 0, true);

            EmergencySaveLog();

            MessageBox.Show($"程序异常: {ex.Message}\n日志已紧急保存",
                "异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // 紧急保存日志
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
                        sw.WriteLine($"请求频率: {_maxRequestsPerSecond}次/秒");
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

        // 复制文件（带重试）
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

        // 删除文件（带重试）
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

        // 读取文件（带重试）
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

        // 导出日志到文件
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
                        finalContent.AppendLine($"机器数量: {txtMachineCount.Text}");
                    });

                    finalContent.AppendLine($"请求方法: {_selectedHttpMethod.Method}");
                    finalContent.AppendLine($"请求频率: {_maxRequestsPerSecond}次/秒");
                    finalContent.AppendLine($"端口上限: {_maxPortUsage}");
                    finalContent.AppendLine($"总请求数: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {(_totalRequestsSent > 0 ? (double)_totalSuccessfulRequests / _totalRequestsSent * 100 : 0):F2}%");
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

        // 添加日志条目
        private void AddLogEntry(string message, int machineId = 0, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string prefix = machineId == 0
                ? message
                : $"[机器 {machineId,2}] {message}";

            string fullMessage = $"[{timestamp}] {prefix}";
            _ = WriteLogToFile(fullMessage, isError);
            _logCache.Enqueue(fullMessage);
        }

        // 异步写入日志文件（兼容C# 7.3，移除await using和DisposeAsync，改用同步Dispose）
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
                    // 初始化文件流和写入器（保持原参数，异步写入）
                    fs = new FileStream(_tempLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true);
                    sw = new StreamWriter(fs, Encoding.UTF8);

                    // 执行写入和刷新
                    await sw.WriteLineAsync(message);
                    await sw.FlushAsync();

                    // 写入成功，直接返回
                    return;
                }
                catch (IOException)
                {
                    retryCount++;
                    // 达到重试上限，写入备用日志
                    if (retryCount >= FileOperationRetryCount)
                    {
                        await WriteToFallbackLog(message);
                        return;
                    }
                    // 未到上限，延迟后重试
                    await Task.Delay(FileOperationRetryDelay);
                }
                finally
                {
                    // .NET Framework 4.6 只有同步Dispose()，替代异步DisposeAsync
                    sw?.Dispose();
                    fs?.Dispose();
                }
            }
        }

        // 备用日志写入（抽离方法，兼容C# 7.3，改用同步Dispose）
        private async Task WriteToFallbackLog(string originalMessage)
        {
            FileStream fallbackFs = null;
            StreamWriter fallbackSw = null;
            try
            {
                string fallbackPath = Path.Combine(_logDirectory, "FallbackLog.txt");
                fallbackFs = new FileStream(fallbackPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true);
                fallbackSw = new StreamWriter(fallbackFs, Encoding.UTF8);

                string fallbackMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [主日志失败] {originalMessage}";
                await fallbackSw.WriteLineAsync(fallbackMessage);
                await fallbackSw.FlushAsync();
            }
            catch
            {
                // 静默忽略备用日志写入失败
            }
            finally
            {
                // 同步释放备用日志资源（.NET Framework 4.6 兼容）
                fallbackSw?.Dispose();
                fallbackFs?.Dispose();
            }
        }
        #endregion

        #region 历史记录相关（适配你单独提取的模型类）
        private const string HISTORY_FILE_PATH = "history.json";

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_isPolling)
                {
                    _isPolling = false;
                    _cts?.Cancel();
                }

                if (_totalPollingTime > TimeSpan.Zero)
                {
                    OutputErrorStatsSummary();
                }

                if (_isLogInitialized && File.Exists(_tempLogFilePath))
                {
                    ExportLogsToFile();
                }

                SaveHistory();
            }
            catch (Exception ex)
            {
                HandleUnhandledException(ex, "窗口关闭异常");
            }
        }

        // 可在 txtMachineCount 的 TextChanged 事件中补充
        private void TxtMachineCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPolling)
            {
                MessageBoxResult result = MessageBox.Show(
                    "当前正在轮询，修改机器数量将停止现有轮询并重新启动，是否确认？",
                    "确认修改", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 停止现有轮询、更新列表、重新启动（原有逻辑）
                    StopPolling_Click(null, null);
                    UpdateMachineList();
                    StartPolling_Click(null, null);
                }
                else
                {
                    // 恢复原有数量，避免输入错误
                    txtMachineCount.Text = _machines.Count.ToString();
                }
            }
            else
            {
                UpdateMachineList();
            }
        }

        private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
            });
        }

        private void LoadHistory()
        {
            Dispatcher.Invoke(() =>
            {
                var historyData = LoadHistoryData();
                lbHistory.ItemsSource = historyData.Items;
                var lastItem = historyData.Items.FirstOrDefault();
                if (lastItem != null)
                {
                    txtEndpoint.Text = lastItem.Endpoint;
                    txtMachineCount.Text = lastItem.MachineCount;
                    txtPollInterval.Text = lastItem.PollInterval;
                    txtParametersJson.Text = lastItem.ParametersJson;
                }
            });
        }

        private void SaveHistory()
        {
            Dispatcher.Invoke(() =>
            {
                var historyData = LoadHistoryData();
                UpdateHistory(txtEndpoint.Text, txtMachineCount.Text, txtPollInterval.Text, txtParametersJson.Text, historyData.Items, 10);
                try
                {
                    File.WriteAllText(HISTORY_FILE_PATH, JsonConvert.SerializeObject(historyData, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存历史记录错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                lbHistory.ItemsSource = null;
                lbHistory.ItemsSource = historyData.Items;
            });
        }

        private HistoryData LoadHistoryData()
        {
            if (File.Exists(HISTORY_FILE_PATH))
            {
                try
                {
                    return JsonConvert.DeserializeObject<HistoryData>(File.ReadAllText(HISTORY_FILE_PATH)) ?? new HistoryData();
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"加载历史记录错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            return new HistoryData();
        }

        private void UpdateHistory(string endpoint, string machineCount, string pollInterval, string parametersJson, List<HistoryItem> items, int maxItems)
        {
            if (!string.IsNullOrWhiteSpace(endpoint) || !string.IsNullOrWhiteSpace(machineCount) ||
                !string.IsNullOrWhiteSpace(pollInterval) || !string.IsNullOrWhiteSpace(parametersJson))
            {
                items.RemoveAll(item => item.Endpoint == endpoint && item.MachineCount == machineCount &&
                                       item.PollInterval == pollInterval && item.ParametersJson == parametersJson);
                items.Insert(0, new HistoryItem
                {
                    Endpoint = endpoint,
                    MachineCount = machineCount,
                    PollInterval = pollInterval,
                    ParametersJson = parametersJson,
                    SavedTime = DateTime.Now
                });
                while (items.Count > maxItems)
                {
                    items.RemoveAt(items.Count - 1);
                }
            }
        }

        private void LbHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (lbHistory.SelectedItem is HistoryItem selectedItem)
                {
                    txtEndpoint.Text = selectedItem.Endpoint;
                    txtMachineCount.Text = selectedItem.MachineCount;
                    txtPollInterval.Text = selectedItem.PollInterval;
                    txtParametersJson.Text = selectedItem.ParametersJson;
                }
            });
        }

        private void LoadFromHistory_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (lbHistory.SelectedItem is HistoryItem selectedItem)
                {
                    txtEndpoint.Text = selectedItem.Endpoint;
                    txtMachineCount.Text = selectedItem.MachineCount;
                    txtPollInterval.Text = selectedItem.PollInterval;
                    txtParametersJson.Text = selectedItem.ParametersJson;
                }
            });
        }

        private void ToggleDetails_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var button = sender as Button;
                if (button != null && button.DataContext is MachineItem machineItem)
                {
                    var detailsTextBlock = button.FindName("detailsTextBlock") as TextBlock;
                    detailsTextBlock.Visibility = detailsTextBlock.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                }
            });
        }
        #endregion

        // 窗口关闭时释放资源
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Dispatcher.Invoke(() =>
            {
                _httpClient?.Dispose();
                _logUiTimer?.Stop();
                _portMonitorTimer?.Stop();
                _statsUpdateTimer?.Stop();
            });
        }
    }
}