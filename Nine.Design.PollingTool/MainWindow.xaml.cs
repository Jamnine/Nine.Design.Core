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
using System.Windows.Media;
using System.Windows.Threading;

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
        private List<Task> _pollingTasks = new List<Task>();
        private HttpClient _httpClient;
        private bool _isPolling = false;
        private SynchronizationContext _uiContext;

        // 日志配置
        private readonly List<Brush> _logBrushes = new List<Brush>
        {
            Brushes.Black, Brushes.DarkBlue, Brushes.DarkGreen, Brushes.DarkRed,
            Brushes.DarkOrange, Brushes.Purple, Brushes.Teal, Brushes.Maroon
        };
        private int _requestTimeoutSeconds = 10;
        private string _tempLogFilePath;
        private string _logDirectory;
        private readonly object _logLock = new object();
        private bool _isLogInitialized = false;
        private const int FileOperationRetryCount = 3;
        private const int FileOperationRetryDelay = 100;

        // 高频请求控制（移除多机器日志缓存，保留全局日志和日志标记）
        private int _maxRequestsPerSecond = 200;
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
                rbGet.Checked += HttpMethodChecked;
                rbPost.Checked += HttpMethodChecked;
                TxtEndpoint_TextChanged(null, null);
                txtEndpoint.TextChanged += TxtEndpoint_TextChanged;
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
                _cts = new CancellationTokenSource();
                UpdateMachineFilterButtons(); // 替换原有UpdateMachineTabItems
                txtMachineCount.ValueChanged += txtMachineCount_ValueChanged;

                if (btnLoadFromHistory != null)
                {
                    btnLoadFromHistory.Click += LoadFromHistory_Click;
                }

                LoadHistory();
                AddLogEntry("[系统] 程序启动，实时统计面板已启用", 0);
                AddLogEntry($"[系统] 默认请求频率：{_maxRequestsPerSecond}次/秒 | 端口上限：{_maxPortUsage}", 0);
            };

            Closing += MainWindow_Closing;
        }

        // 窗口大小改变事件（保持不变）
        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double scale = Math.Max(0.8, Math.Min(1.5, e.NewSize.Width / 1400));
            this.FontSize = 12 * scale;
        }

        #region 初始化方法（保持原有，仅修改日志UI定时器逻辑）
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
                    AddLogEntry("[系统] TCP端口重用已启用", 0);
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

                AddLogEntry("[系统] HttpClient初始化完成（连接池大小：1000，已启用SSL证书忽略）", 0);
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
            _logUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _logUiTimer.Tick += LogUiTimer_Tick;
            _logUiTimer.Start();
            AddLogEntry($"[系统] 日志UI定时器已启动，间隔：{_logUiTimer.Interval.TotalMilliseconds}ms", 0);
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

        #region 定时器回调（重构：日志UI更新 + 统计更新）
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
        private void UpdateGlobalLogUI()
        {
            if (_globalLogQueue.IsEmpty) return;

            // 1. 出队新日志，加入过滤列表
            List<LogEntry> newLogs = new List<LogEntry>();
            int maxBatchSize = 100;
            int count = 0;

            LogEntry logEntry;
            while (_globalLogQueue.TryDequeue(out logEntry) && count < maxBatchSize)
            {
                newLogs.Add(logEntry);
                _filteredLogList.Add(logEntry);
                count++;
            }

            // 2. 过滤日志（根据当前选中的机器ID）
            List<LogEntry> logsToDisplay = new List<LogEntry>();
            if (_currentFilterMachineId == 0)
            {
                logsToDisplay = _filteredLogList.ToList();
            }
            else
            {
                logsToDisplay = _filteredLogList.Where(l => l.MachineId == _currentFilterMachineId).ToList();
            }

            // 3. 更新UI（先清空原有，再添加过滤后的日志，避免重复）
            Dispatcher.Invoke(() =>
            {
                // 限制日志最大数量，防止内存溢出
                if (_filteredLogList.Count > 50000)
                {
                    int removeCount = _filteredLogList.Count - 50000;
                    _filteredLogList.RemoveRange(0, removeCount);
                }

                // 清空当前日志列表，重新添加过滤后的日志
                lbGlobalLogs.Items.Clear();
                foreach (var log in logsToDisplay)
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

                // 滚动到最新日志
                if (lbGlobalLogs.Items.Count > 0)
                {
                    lbGlobalLogs.ScrollIntoView(lbGlobalLogs.Items[lbGlobalLogs.Items.Count - 1]);
                }
            });
        }
        #endregion

        #region 核心业务逻辑（重构：机器按钮 + 日志过滤 + 统计切换）
        private void HttpMethodChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is RadioButton radioButton && radioButton.IsChecked.HasValue && radioButton.IsChecked.Value)
                {
                    _selectedHttpMethod = (radioButton == rbGet) ? HttpMethod.Get : HttpMethod.Post;
                    AddLogEntry($"[系统] 请求方法已切换为: {_selectedHttpMethod.Method}", 0);

                    Dispatcher.Invoke(() =>
                    {
                        rbGet.IsChecked = (radioButton == rbGet);
                        rbPost.IsChecked = (radioButton == rbPost);
                    });
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 切换请求方法失败：{ex.Message}", 0, true);
            }
        }

        /// <summary>
        /// 重构：创建/更新机器过滤按钮（替换原有Tab页面创建）
        /// </summary>
        private void UpdateMachineFilterButtons()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. 获取当前机器数量
                    int machineCount = 1;
                    if (txtMachineCount.Value.HasValue && txtMachineCount.Value.Value > 0)
                    {
                        machineCount = (int)txtMachineCount.Value.Value;
                    }

                    // 2. 清空原有机器按钮（保留「全部日志」按钮）
                    var machineButtons = spMachineFilterButtons.Children.OfType<Button>().Where(b => b.Name != "btnAllLogs").ToList();
                    foreach (var btn in machineButtons)
                    {
                        spMachineFilterButtons.Children.Remove(btn);
                    }

                    // 3. 清空原有机器数据
                    _allMachineData.Clear();
                    // 注释：_machineLogCaches 已移除，无需兼容

                    // 4. 初始化全局统计数据
                    var totalOverviewData = new MachineDataModel();
                    _allMachineData.Add(0, totalOverviewData);
                    _currentDisplayData = totalOverviewData;

                    // 5. 创建机器按钮和对应数据
                    for (int i = 1; i <= machineCount; i++)
                    {
                        var machineData = new MachineDataModel();
                        _allMachineData.Add(i, machineData);

                        // 创建机器过滤按钮
                        Button machineBtn = new Button
                        {
                            Content = $"机器 {i}",
                            Style = (Style)FindResource("MachineFilterButtonStyle"),
                            Tag = i // 存储机器ID
                        };
                        machineBtn.Click += BtnMachineFilter_Click;
                        spMachineFilterButtons.Children.Add(machineBtn);
                    }

                    // 6. 打印日志
                    AddLogEntry($"[系统] 机器过滤按钮创建成功，当前机器数量: {machineCount}", 0);
                }
                catch (Exception ex)
                {
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
                // 修正点1：Dictionary 改用 TryGetValue 替代 GetValueOrDefault（兼容所有框架版本）
                if (_allMachineData.TryGetValue(machineId, out MachineDataModel machineData))
                {
                    _currentDisplayData = machineData;
                }
                else
                {
                    // 若未找到，使用默认实例（保持原有逻辑）
                    _currentDisplayData = new MachineDataModel();
                }

                UpdateGlobalLogUI(); // 强制刷新日志
                StatsUpdateTimer_Tick(null, null); // 强制刷新统计

                AddLogEntry($"[系统] 已切换到「机器 {machineId}」的日志和统计", 0);
            }
        }

        /// <summary>
        /// 全部日志按钮点击事件（新增）
        /// </summary>
        private void BtnAllLogs_Click(object sender, EventArgs e)
        {
            // 1. 重置过滤机器ID为0（全部）
            _currentFilterMachineId = 0;

            // 2. 更新按钮样式
            ResetFilterButtonStyles();
            btnAllLogs.Background = (SolidColorBrush)FindResource("PrimaryColor");

            // 3. 更新统计数据和日志UI
            // 修正点2：Dictionary 改用 TryGetValue 替代 GetValueOrDefault
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

            AddLogEntry("[系统] 已切换到「全部日志」和全局统计", 0);
        }

        /// <summary>
        /// 重置过滤按钮样式（辅助方法，新增）
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
            AddLogEntry($"轮询任务已启动（高频模式）", machineId);
            Dictionary<string, int> machineStats = new Dictionary<string, int>
            {
                { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                { "端口超限", 0 }, { "频率限制", 0 }
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

                        // 频率控制
                        TimeSpan delayTime = TimeSpan.Zero;
                        if (_maxRequestsPerSecond > 0)
                        {
                            // 修正点3：ConcurrentDictionary 改用 TryGetValue 替代 GetValueOrDefault
                            DateTime lastTime = DateTime.MinValue;
                            if (_machineLastRequestTime.TryGetValue(machineId, out DateTime storedLastTime))
                            {
                                lastTime = storedLastTime;
                            }

                            var minInterval = TimeSpan.FromSeconds(1.0 / _maxRequestsPerSecond);
                            var currentTime = DateTime.Now;
                            if (currentTime - lastTime < minInterval)
                            {
                                delayTime = minInterval - (currentTime - lastTime);
                                machineStats["频率限制"]++;
                            }
                            _machineLastRequestTime[machineId] = DateTime.Now;

                            if (delayTime > TimeSpan.Zero)
                            {
                                await Task.Delay(delayTime, cancellationToken).ConfigureAwait(false);
                            }
                        }

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
                            // 修正点4：ConcurrentDictionary 改用 TryGetValue 替代 GetValueOrDefault
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
                int userMaxRequests = 0;
                if (int.TryParse(txtPollInterval.Value.ToString(), out userMaxRequests) && userMaxRequests > 0)
                {
                    _maxRequestsPerSecond = userMaxRequests;
                    AddLogEntry($"[系统] 请求频率更新为：{_maxRequestsPerSecond}次/秒", 0);
                }

                UpdateMachineFilterButtons();
                await StartPollingAsync();
                SaveHistory();
            });
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

                    int tempMachineCount;
                    if (!int.TryParse(txtMachineCount.Value.ToString(), out tempMachineCount) || tempMachineCount <= 0)
                    {
                        AddLogEntry("[系统] 机器数量无效", 0);
                        MessageBox.Show("机器数量必须是大于0的整数", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new InvalidOperationException("机器数量无效");
                    }

                    AddLogEntry($"[系统] 频率限制: {_maxRequestsPerSecond}次/秒 | 端口上限: {_maxPortUsage}", 0);
                });

                string endpoint = await Dispatcher.InvokeAsync(() => txtEndpoint.Text);
                HttpMethod requestMethod = _selectedHttpMethod;
                int machineCount = await Dispatcher.InvokeAsync(() =>
                {
                    int tempCount;
                    int.TryParse(txtMachineCount.Value.ToString(), out tempCount);
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
                    AddLogEntry($"[系统] 高频轮询已开始. 机器数: {machineCount}, 频率: {_maxRequestsPerSecond}次/秒", 0);
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

            AddLogEntry("[系统] 高频轮询已停止", 0);
            double successRate = 0;
            if (_totalRequestsSent > 0)
            {
                successRate = (double)_totalSuccessfulRequests / _totalRequestsSent * 100;
            }
            AddLogEntry($"[统计] 总请求: {_totalRequestsSent} | 成功: {_totalSuccessfulRequests} | 成功率: {successRate:F2}%", 0);
        }
        #endregion

        #region 辅助方法（重构：日志条目 + 移除Tab相关查找）
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

                AddLogEntry($"[全局] 总时长: {timeFormat} | 频率: {_maxRequestsPerSecond}次/秒 | 端口上限: {_maxPortUsage}", 0);
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
                    // 修正点5：ConcurrentDictionary 改用 TryGetValue 替代 GetValueOrDefault
                    Dictionary<string, int> stats = new Dictionary<string, int>();
                    if (_errorStats.TryGetValue(machineId, out Dictionary<string, int> storedStats))
                    {
                        stats = storedStats;
                    }

                    // 修正点6：ConcurrentDictionary 改用 TryGetValue 替代 GetValueOrDefault（原有逻辑保留，仅替换取值方式）
                    Tuple<double, double> extremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                    if (_responseTimeExtremes.TryGetValue(machineId, out Tuple<double, double> storedExtremes))
                    {
                        extremes = storedExtremes;
                    }

                    int machineTotal = kvp.Value;
                    // 修正1：_machineSuccessRequests 是 ConcurrentDictionary，改用 TryGetValue 替代 GetValueOrDefault
                    int machineSuccess = 0; // 先初始化默认值
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

                    // 修正2：stats 是 Dictionary<string, int>，兼容低版本框架，改用 TryGetValue 替代 GetValueOrDefault
                    // 先初始化各错误类型的默认值为 0
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

        private void LoadFromHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PollingHistory.config");
                if (!File.Exists(historyPath))
                {
                    AddLogEntry("[系统] 无历史配置文件可加载", 0, true);
                    MessageBox.Show("未找到历史配置文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string content = ReadFileContentWithRetry(historyPath);
                var historyData = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                if (historyData == null || historyData.Count == 0)
                {
                    AddLogEntry("[系统] 历史配置文件为空，无法加载", 0, true);
                    MessageBox.Show("历史配置文件内容无效", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (historyData.ContainsKey("Endpoint"))
                    {
                        txtEndpoint.Text = historyData["Endpoint"];
                    }

                    int machineCount = 1;
                    if (historyData.ContainsKey("MachineCount") && int.TryParse(historyData["MachineCount"], out machineCount))
                    {
                        txtMachineCount.Value = machineCount;
                    }

                    int reqPerSec = 200;
                    if (historyData.ContainsKey("MaxRequestsPerSecond") && int.TryParse(historyData["MaxRequestsPerSecond"], out reqPerSec))
                    {
                        txtPollInterval.Value = reqPerSec;
                    }

                    if (historyData.ContainsKey("HttpMethod"))
                    {
                        if (historyData["HttpMethod"] == "Get")
                        {
                            rbGet.IsChecked = true;
                        }
                        else
                        {
                            rbPost.IsChecked = true;
                        }
                    }

                    if (historyData.ContainsKey("ParametersJson"))
                    {
                        txtParametersJson.Text = historyData["ParametersJson"];
                    }
                });

                AddLogEntry("[系统] 历史配置已成功加载", 0);
                MessageBox.Show("历史配置加载完成", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLogEntry($"[系统] 加载历史配置失败：{ex.Message}", 0, true);
                MessageBox.Show($"加载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                    finalContent.AppendLine($"请求频率: {_maxRequestsPerSecond}次/秒");
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

        /// <summary>
        /// 重构：添加带机器标记的日志条目
        /// </summary>
        private void AddLogEntry(string message, int machineId = 0, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string prefix = machineId == 0 ? message : $"[机器 {machineId,2}] {message}";
            string fullMessage = $"[{timestamp}] {prefix}";

            // 入队带标记的日志条目
            _globalLogQueue.Enqueue(new LogEntry
            {
                Message = fullMessage,
                MachineId = machineId,
                IsError = isError,
                Timestamp = DateTime.Now
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
                    var historyData = JsonConvert.DeserializeObject<HistoryData>(content);
                    if (historyData != null)
                    {
                        lbHistory.ItemsSource = historyData.Items;
                        AddLogEntry("[系统] 历史配置加载成功", 0);
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
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PollingHistory.config");
                var historyData = new Dictionary<string, string>
                {
                    { "Endpoint", txtEndpoint.Text },
                    { "MachineCount", txtMachineCount.Value.ToString() },
                    { "MaxRequestsPerSecond", txtPollInterval.Value.ToString() },
                    { "HttpMethod", _selectedHttpMethod.Method },
                    { "ParametersJson", txtParametersJson.Text }
                };

                string content = JsonConvert.SerializeObject(historyData, Formatting.Indented);
                using (var fs = new FileStream(historyPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    sw.Write(content);
                    sw.Flush();
                }
                AddLogEntry("[系统] 当前配置已保存到历史记录", 0);
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
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }

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

        private void LbHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (lbHistory.SelectedItem is HistoryItem selectedItem)
                {
                    txtEndpoint.Text = selectedItem.Endpoint;
                    txtMachineCount.Value = Convert.ToDouble(selectedItem.MachineCount);
                    txtPollInterval.Value = Convert.ToDouble(selectedItem.PollInterval);
                    txtParametersJson.Text = selectedItem.ParametersJson;
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

        #region 已移除的字段（兼容旧代码，标记为过时）
        [Obsolete("重构后已移除，使用_globalLogQueue替代")]
        private Dictionary<int, ConcurrentQueue<string>> _machineLogCaches = new Dictionary<int, ConcurrentQueue<string>>();
        #endregion
    }

    /// <summary>
    /// 日志条目模型（新增：带机器标记）
    /// </summary>
    public class LogEntry
    {
        public string Message { get; set; }
        public int MachineId { get; set; }
        public bool IsError { get; set; }
        public DateTime Timestamp { get; set; }
    }

    //// 补充缺失的模型类（避免编译报错）
    //public class MachineDataModel { }
    //public class HistoryData
    //{
    //    public List<HistoryItem> Items { get; set; } = new List<HistoryItem>();
    //}
    //public class HistoryItem
    //{
    //    public string Endpoint { get; set; }
    //    public int MachineCount { get; set; }
    //    public int PollInterval { get; set; }
    //    public string ParametersJson { get; set; }
    //}
}