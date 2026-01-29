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
    /// 主窗口（完整功能，兼容C# 7.3 / .NET Framework 4.6）
    /// </summary>
    public partial class MainWindow : WindowX, INotifyPropertyChanged
    {
        // 基础配置（保持原有不变，此处省略重复代码，仅展示修复点）
        private ObservableCollection<MachineTabItemModel> _machineTabItems = new ObservableCollection<MachineTabItemModel>();
        private Dictionary<int, MachineDataModel> _allMachineData = new Dictionary<int, MachineDataModel>();
        private MachineDataModel _totalOverviewData = new MachineDataModel();
        private HttpMethod _selectedHttpMethod = HttpMethod.Post;
        private CancellationTokenSource _cts;
        private List<Task> _pollingTasks = new List<Task>();
        private HttpClient _httpClient;
        private bool _isPolling = false;
        private SynchronizationContext _uiContext;

        // 日志配置（保持原有不变）
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

        // 高频请求控制（保持原有不变）
        private int _maxRequestsPerSecond = 200;
        private readonly ConcurrentQueue<string> _totalLogCache = new ConcurrentQueue<string>();
        private Dictionary<int, ConcurrentQueue<string>> _machineLogCaches = new Dictionary<int, ConcurrentQueue<string>>();
        private DispatcherTimer _logUiTimer;

        // 端口监控与统计（保持原有不变）
        private int _maxPortUsage = 50000;
        private DispatcherTimer _portMonitorTimer;
        private int _currentPortCount = 0;
        private int _totalRequestsSent = 0;
        private int _totalSuccessfulRequests = 0;
        private readonly object _requestCountLock = new object();
        private DateTime _pollingStartTime;
        private DispatcherTimer _statsUpdateTimer;

        // 多机器统计（保持原有不变）
        private readonly ConcurrentDictionary<int, int> _machineTotalRequests = new ConcurrentDictionary<int, int>();
        private readonly ConcurrentDictionary<int, int> _machineSuccessRequests = new ConcurrentDictionary<int, int>();
        private readonly ConcurrentDictionary<int, Dictionary<string, int>> _errorStats = new ConcurrentDictionary<int, Dictionary<string, int>>();
        private readonly ConcurrentDictionary<int, Tuple<double, double>> _responseTimeExtremes = new ConcurrentDictionary<int, Tuple<double, double>>();
        private readonly ConcurrentDictionary<int, DateTime> _machineLastRequestTime = new ConcurrentDictionary<int, DateTime>();
        private TimeSpan _totalPollingTime = TimeSpan.Zero;

        // 构造函数（保持原有不变，仅确保事件绑定完整）
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

            tcMachineOverview.ItemsSource = _machineTabItems;

            Loaded += (s, e) =>
            {
                _uiContext = SynchronizationContext.Current;
                rbGet.Checked += HttpMethodChecked;
                rbPost.Checked += HttpMethodChecked;
                TxtEndpoint_TextChanged(null, null);
                txtEndpoint.TextChanged += TxtEndpoint_TextChanged;
                btnStartPolling.IsEnabled = !string.IsNullOrWhiteSpace(txtEndpoint.Text);
                _cts = new CancellationTokenSource();
                UpdateMachineTabItems();
                // 绑定Panuon专属值变更事件（关键：匹配补全的方法签名）
                txtMachineCount.ValueChanged += txtMachineCount_ValueChanged;

                // 绑定历史记录加载按钮事件
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

        // 窗口大小改变事件（保持原有不变）
        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double scale = Math.Max(0.8, Math.Min(1.5, e.NewSize.Width / 1400));
            this.FontSize = 12 * scale;
        }

        #region 初始化方法（保持原有不变，此处省略重复代码）
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

        #region 定时器回调（修复关键：FindVisualChild 调用指定具体类型 ListBox）
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
                    _totalOverviewData.TotalRequests = _totalRequestsSent;
                    _totalOverviewData.SuccessRequests = _totalSuccessfulRequests;

                    foreach (var kvp in _machineTotalRequests)
                    {
                        int machineId = kvp.Key;
                        if (_allMachineData.ContainsKey(machineId))
                        {
                            _allMachineData[machineId].TotalRequests = kvp.Value;
                            int success = 0;
                            if (_machineSuccessRequests.ContainsKey(machineId))
                            {
                                success = _machineSuccessRequests[machineId];
                            }
                            _allMachineData[machineId].SuccessRequests = success;
                        }
                    }

                    OnPropertyChanged(nameof(RunTime));
                });
            }
            catch (Exception ex)
            {
                AddLogEntry($"[统计] UI更新失败：{ex.Message}", 0);
            }
        }

        private void LogUiTimer_Tick(object sender, EventArgs e)
        {
            UpdateTabLogUI(0, _totalLogCache);
            foreach (var kvp in _machineLogCaches)
            {
                UpdateTabLogUI(kvp.Key, kvp.Value);
            }
        }

        // 核心修复：UpdateTabLogUI 中调用 FindVisualChild 时指定具体类型 ListBox
        private void UpdateTabLogUI(int machineId, ConcurrentQueue<string> logCache)
        {
            if (logCache.IsEmpty) return;

            List<string> logsToAdd = new List<string>();
            int maxBatchSize = 100;
            int count = 0;

            string log;
            while (logCache.TryDequeue(out log) && count < maxBatchSize)
            {
                logsToAdd.Add(log);
                count++;
            }

            // 找到对应页面的ListBox（修复：调用 FindVisualChild<ListBox> 明确类型）
            ListBox targetListBox = null;
            if (machineId == 0)
            {
                MachineTabItemModel totalTab = null;
                foreach (var tab in _machineTabItems)
                {
                    if (tab.MachineId == 0)
                    {
                        totalTab = tab;
                        break;
                    }
                }
                if (totalTab != null && tcMachineOverview.ItemContainerGenerator.ContainerFromItem(totalTab) is TabItem totalTabItem)
                {
                    // 修复：指定泛型类型为 ListBox，不再使用 T
                    targetListBox = FindVisualChild<ListBox>(totalTabItem, "lbTabLogs");
                }
            }
            else
            {
                MachineTabItemModel machineTab = null;
                foreach (var tab in _machineTabItems)
                {
                    if (tab.MachineId == machineId)
                    {
                        machineTab = tab;
                        break;
                    }
                }
                if (machineTab != null && tcMachineOverview.ItemContainerGenerator.ContainerFromItem(machineTab) is TabItem machineTabItem)
                {
                    // 修复：指定泛型类型为 ListBox，不再使用 T
                    targetListBox = FindVisualChild<ListBox>(machineTabItem, "lbTabLogs");
                }
            }

            if (targetListBox == null) return;

            foreach (var logItemContent in logsToAdd)
            {
                bool isError = logItemContent.Contains("错误:") || logItemContent.Contains("警告:");
                int targetMachineId = machineId;

                ListBoxItem logItem = new ListBoxItem
                {
                    Content = logItemContent,
                    Padding = new Thickness(2)
                };

                if (isError)
                {
                    logItem.Foreground = Brushes.Red;
                    logItem.FontWeight = FontWeights.Bold;
                }
                else if (targetMachineId > 0)
                {
                    int colorIndex = (targetMachineId - 1) % _logBrushes.Count;
                    logItem.Foreground = _logBrushes[colorIndex];
                }
                else
                {
                    logItem.Foreground = Brushes.DarkGray;
                }

                if (targetListBox.Items.Count > 50000)
                {
                    for (int i = 0; i < 1000 && targetListBox.Items.Count > 0; i++)
                    {
                        targetListBox.Items.RemoveAt(0);
                    }
                }

                targetListBox.Items.Add(logItem);
            }

            if (logsToAdd.Count > 0 && targetListBox.Items.Count > 0)
            {
                targetListBox.ScrollIntoView(targetListBox.Items[targetListBox.Items.Count - 1]);
            }
        }
        #endregion

        #region 核心业务逻辑（保持原有不变，此处省略重复代码）
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

        private void UpdateMachineTabItems()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (txtMachineCount == null || !txtMachineCount.Value.HasValue || txtMachineCount.Value <= 0)
                    {
                        AddLogEntry("[系统] 机器数量输入框无效或值非正整数，无法创建页面", 0);
                        return;
                    }

                    int machineCount = (int)txtMachineCount.Value.Value;

                    _machineTabItems.Clear();
                    _allMachineData.Clear();
                    _machineLogCaches.Clear();
                    ClearConcurrentQueue(_totalLogCache);
                    _totalOverviewData = new MachineDataModel { MachineId = 0, TabTitle = "总览" };

                    var totalTabItem = new MachineTabItemModel
                    {
                        TabTitle = "总览",
                        MachineId = 0,
                        MachineData = _totalOverviewData
                    };
                    _machineTabItems.Add(totalTabItem);

                    for (int i = 1; i <= machineCount; i++)
                    {
                        Dictionary<string, int> errorDict = new Dictionary<string, int>
                        {
                            { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                            { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                            { "端口超限", 0 }, { "频率限制", 0 }
                        };
                        _errorStats[i] = errorDict;

                        Tuple<double, double> responseTuple = new Tuple<double, double>(double.MaxValue, double.MinValue);
                        _responseTimeExtremes[i] = responseTuple;

                        _machineLastRequestTime[i] = DateTime.MinValue;

                        var machineData = new MachineDataModel
                        {
                            MachineId = i,
                            IsPolling = false,
                            TotalRequests = 0,
                            SuccessRequests = 0,
                            ErrorStats = new Dictionary<string, int>(_errorStats[i]),
                            ResponseTimeExtremes = _responseTimeExtremes[i]
                        };
                        _allMachineData.Add(i, machineData);
                        _machineLogCaches.Add(i, new ConcurrentQueue<string>());

                        var machineTabItem = new MachineTabItemModel
                        {
                            TabTitle = $"机器 {i}",
                            MachineId = i,
                            MachineData = machineData
                        };
                        _machineTabItems.Add(machineTabItem);
                    }

                    ResetAllStats();

                    AddLogEntry($"[系统] 多页面创建成功，当前页面数量: {_machineTabItems.Count}（含总览）", 0);
                }
                catch (Exception ex)
                {
                    AddLogEntry($"[系统] 创建多页面失败：{ex.Message}，异常堆栈：{ex.StackTrace}", 0, true);
                }
            });
        }

        private void ResetAllStats()
        {
            lock (_requestCountLock)
            {
                _totalRequestsSent = 0;
                _totalSuccessfulRequests = 0;
            }

            _totalOverviewData.TotalRequests = 0;
            _totalOverviewData.SuccessRequests = 0;
            _totalOverviewData.ErrorStats = new Dictionary<string, int>
            {
                { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                { "端口超限", 0 }, { "频率限制", 0 }
            };
            _totalOverviewData.ResponseTimeExtremes = new Tuple<double, double>(double.MaxValue, double.MinValue);

            foreach (var kvp in _allMachineData)
            {
                var machineData = kvp.Value;
                machineData.TotalRequests = 0;
                machineData.SuccessRequests = 0;
                machineData.ErrorStats = new Dictionary<string, int>
                {
                    { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                    { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                    { "端口超限", 0 }, { "频率限制", 0 }
                };
                machineData.ResponseTimeExtremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                machineData.LastRequestTime = DateTime.MinValue;
            }

            _machineTotalRequests.Clear();
            _machineSuccessRequests.Clear();
            _errorStats.Clear();
            _responseTimeExtremes.Clear();
            _machineLastRequestTime.Clear();
        }

        private void ClearLogs()
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var tabItem in _machineTabItems)
                {
                    if (tcMachineOverview.ItemContainerGenerator.ContainerFromItem(tabItem) is TabItem uiTabItem)
                    {
                        var listBox = FindVisualChild<ListBox>(uiTabItem, "lbTabLogs");
                        if (listBox != null)
                        {
                            listBox.Items.Clear();
                        }
                    }
                }
            });

            ClearConcurrentQueue(_totalLogCache);
            foreach (var kvp in _machineLogCaches)
            {
                ClearConcurrentQueue(kvp.Value);
            }

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
                _totalRequestsSent++;
                if (_machineTotalRequests.ContainsKey(machineId))
                {
                    _machineTotalRequests[machineId] = _machineTotalRequests[machineId] + 1;
                }
                else
                {
                    _machineTotalRequests[machineId] = 1;
                }

                if (isSuccess)
                {
                    _totalSuccessfulRequests++;
                    if (_machineSuccessRequests.ContainsKey(machineId))
                    {
                        _machineSuccessRequests[machineId] = _machineSuccessRequests[machineId] + 1;
                    }
                    else
                    {
                        _machineSuccessRequests[machineId] = 1;
                    }
                }
            }
        }

        private async Task PollEndpoint(int machineId, string endpoint, HttpMethod requestMethod, CancellationToken cancellationToken)
        {
            AddLogEntry($"轮询任务已启动（高频模式）", machineId);
            Dictionary<string, int> machineStats = null;
            if (_errorStats.ContainsKey(machineId))
            {
                machineStats = _errorStats[machineId];
            }
            else
            {
                machineStats = new Dictionary<string, int>
                {
                    { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
                    { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
                    { "端口超限", 0 }, { "频率限制", 0 }
                };
                _errorStats[machineId] = machineStats;
            }
            int maxRetryCount = 3;

            while (!cancellationToken.IsCancellationRequested && _isPolling)
            {
                if (IsPortUsageExceeded())
                {
                    lock (machineStats)
                    {
                        if (machineStats.ContainsKey("端口超限"))
                        {
                            machineStats["端口超限"] = machineStats["端口超限"] + 1;
                        }
                        else
                        {
                            machineStats["端口超限"] = 1;
                        }
                    }
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

                        TimeSpan delayTime = TimeSpan.Zero;
                        if (_maxRequestsPerSecond > 0)
                        {
                            DateTime lastTime = DateTime.MinValue;
                            if (_machineLastRequestTime.ContainsKey(machineId))
                            {
                                lastTime = _machineLastRequestTime[machineId];
                            }
                            else
                            {
                                _machineLastRequestTime[machineId] = DateTime.MinValue;
                            }
                            var minInterval = TimeSpan.FromSeconds(1.0 / _maxRequestsPerSecond);
                            var currentTime = DateTime.Now;
                            if (currentTime - lastTime < minInterval)
                            {
                                delayTime = minInterval - (currentTime - lastTime);
                                lock (machineStats)
                                {
                                    if (machineStats.ContainsKey("频率限制"))
                                    {
                                        machineStats["频率限制"] = machineStats["频率限制"] + 1;
                                    }
                                    else
                                    {
                                        machineStats["频率限制"] = 1;
                                    }
                                }
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

                        string jsonParameters = null;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            jsonParameters = GetJsonParameters();
                        });

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
                                lock (machineStats)
                                {
                                    if (machineStats.ContainsKey("参数错误"))
                                    {
                                        machineStats["参数错误"] = machineStats["参数错误"] + 1;
                                    }
                                    else
                                    {
                                        machineStats["参数错误"] = 1;
                                    }
                                }
                                IncrementRequestCount(machineId, false);
                                isRequestSuccess = true;
                                break;
                            }
                        }

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
                                        result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        isSuccess = response.IsSuccessStatusCode;

                                        TimeSpan responseTime = DateTime.Now - requestStartTime;
                                        double responseTimeMs = responseTime.TotalMilliseconds;
                                        Tuple<double, double> currentExtremes = null;
                                        if (_responseTimeExtremes.ContainsKey(machineId))
                                        {
                                            currentExtremes = _responseTimeExtremes[machineId];
                                        }
                                        else
                                        {
                                            currentExtremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                                            _responseTimeExtremes[machineId] = currentExtremes;
                                        }
                                        _responseTimeExtremes[machineId] = new Tuple<double, double>(
                                            Math.Min(currentExtremes.Item1, responseTimeMs),
                                            Math.Max(currentExtremes.Item2, responseTimeMs));

                                        if (!isSuccess)
                                        {
                                            AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 状态码: {(int)response.StatusCode} | 结果: {result}", machineId);
                                            lock (machineStats)
                                            {
                                                if (machineStats.ContainsKey("HTTP错误"))
                                                {
                                                    machineStats["HTTP错误"] = machineStats["HTTP错误"] + 1;
                                                }
                                                else
                                                {
                                                    machineStats["HTTP错误"] = 1;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            AddLogEntry($"{requestMethod.Method} | 响应: {responseTimeMs:F2}ms | 结果: {result}", machineId);
                                            lock (machineStats)
                                            {
                                                if (machineStats.ContainsKey("成功次数"))
                                                {
                                                    machineStats["成功次数"] = machineStats["成功次数"] + 1;
                                                }
                                                else
                                                {
                                                    machineStats["成功次数"] = 1;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        lock (machineStats)
                                        {
                                            if (machineStats.ContainsKey("其他错误"))
                                            {
                                                machineStats["其他错误"] = machineStats["其他错误"] + 1;
                                            }
                                            else
                                            {
                                                machineStats["其他错误"] = 1;
                                            }
                                        }
                                        AddLogEntry($"响应处理错误: {ex.Message}", machineId, true);
                                    }

                                    IncrementRequestCount(machineId, isSuccess);

                                }
                                catch (Exception ex)
                                {
                                    IncrementRequestCount(machineId, false);
                                    AddLogEntry($"响应处理异常: {ex.Message}", machineId, true);
                                    lock (machineStats)
                                    {
                                        if (machineStats.ContainsKey("其他错误"))
                                        {
                                            machineStats["其他错误"] = machineStats["其他错误"] + 1;
                                        }
                                        else
                                        {
                                            machineStats["其他错误"] = 1;
                                        }
                                    }
                                }
                            }, cancellationToken).ConfigureAwait(false);
                        }

                        isRequestSuccess = true;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("每个套接字地址") || ex.Message.Contains("only use once"))
                    {
                        retryCount++;
                        lock (machineStats)
                        {
                            if (machineStats.ContainsKey("网络错误"))
                            {
                                machineStats["网络错误"] = machineStats["网络错误"] + 1;
                            }
                            else
                            {
                                machineStats["网络错误"] = 1;
                            }
                        }
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
                            lock (machineStats)
                            {
                                if (machineStats.ContainsKey("超时"))
                                {
                                    machineStats["超时"] = machineStats["超时"] + 1;
                                }
                                else
                                {
                                    machineStats["超时"] = 1;
                                }
                            }
                            AddLogEntry($"错误: 请求超时（{_requestTimeoutSeconds}秒）", machineId, true);
                            IncrementRequestCount(machineId, false);
                        }
                        isRequestSuccess = true;
                        break;
                    }
                    catch (HttpRequestException ex)
                    {
                        lock (machineStats)
                        {
                            if (machineStats.ContainsKey("网络错误"))
                            {
                                machineStats["网络错误"] = machineStats["网络错误"] + 1;
                            }
                            else
                            {
                                machineStats["网络错误"] = 1;
                            }
                        }
                        AddLogEntry($"错误: 网络异常 - {ex.Message}", machineId, true);
                        IncrementRequestCount(machineId, false);
                        retryCount = maxRetryCount;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lock (machineStats)
                        {
                            if (machineStats.ContainsKey("其他错误"))
                            {
                                machineStats["其他错误"] = machineStats["其他错误"] + 1;
                            }
                            else
                            {
                                machineStats["其他错误"] = 1;
                            }
                        }
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

                UpdateMachineTabItems();
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

        #region 辅助方法（补全缺失的两个方法，修复 FindVisualChild 签名）
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
                    Dictionary<string, int> stats = new Dictionary<string, int>();
                    if (_errorStats.ContainsKey(machineId))
                    {
                        stats = _errorStats[machineId];
                    }
                    Tuple<double, double> extremes = new Tuple<double, double>(double.MaxValue, double.MinValue);
                    if (_responseTimeExtremes.ContainsKey(machineId))
                    {
                        extremes = _responseTimeExtremes[machineId];
                    }

                    int machineTotal = kvp.Value;
                    int machineSuccess = 0;
                    if (_machineSuccessRequests.ContainsKey(machineId))
                    {
                        machineSuccess = _machineSuccessRequests[machineId];
                    }
                    double successRate = 0;
                    if (machineTotal > 0)
                    {
                        successRate = (double)machineSuccess / machineTotal * 100;
                    }

                    string minTime = "无数据";
                    string maxTime = "无数据";
                    if (machineSuccess > 0)
                    {
                        minTime = $"{extremes.Item1:F2}ms";
                        maxTime = $"{extremes.Item2:F2}ms";
                    }

                    int timeoutCount = 0;
                    if (stats.ContainsKey("超时")) timeoutCount = stats["超时"];

                    int networkErrorCount = 0;
                    if (stats.ContainsKey("网络错误")) networkErrorCount = stats["网络错误"];

                    int httpErrorCount = 0;
                    if (stats.ContainsKey("HTTP错误")) httpErrorCount = stats["HTTP错误"];

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

        // 补全缺失：从历史记录加载按钮点击事件（LoadFromHistory_Click）
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

                // 读取并解析历史配置
                string content = ReadFileContentWithRetry(historyPath);
                var historyData = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                if (historyData == null || historyData.Count == 0)
                {
                    AddLogEntry("[系统] 历史配置文件为空，无法加载", 0, true);
                    MessageBox.Show("历史配置文件内容无效", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 同步到UI控件
                Dispatcher.Invoke(() =>
                {
                    // 加载Endpoint
                    if (historyData.ContainsKey("Endpoint"))
                    {
                        txtEndpoint.Text = historyData["Endpoint"];
                    }

                    // 加载机器数量
                    int machineCount = 1;
                    if (historyData.ContainsKey("MachineCount") && int.TryParse(historyData["MachineCount"], out machineCount))
                    {
                        txtMachineCount.Value = machineCount;
                    }

                    // 加载请求频率
                    int reqPerSec = 200;
                    if (historyData.ContainsKey("MaxRequestsPerSecond") && int.TryParse(historyData["MaxRequestsPerSecond"], out reqPerSec))
                    {
                        txtPollInterval.Value = reqPerSec;
                    }

                    // 加载请求方法
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

                    // 加载JSON参数
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

        // 补全缺失：Panuon 控件专属值变更事件（txtMachineCount_ValueChanged）
        private void txtMachineCount_ValueChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<double?> e)
        {
            try
            {
                // 确保值有效后更新机器标签页
                if (e.NewValue.HasValue && e.NewValue.Value > 0)
                {
                    UpdateMachineTabItems();
                    AddLogEntry($"[系统] 机器数量已更新为：{(int)e.NewValue.Value}", 0);
                }
                else if (e.NewValue.HasValue && e.NewValue.Value <= 0)
                {
                    AddLogEntry("[系统] 机器数量不能小于等于0，已忽略无效值", 0, true);
                    // 重置为默认值1
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
                    double exportSuccessRate = 0;
                    if (_totalRequestsSent > 0)
                    {
                        exportSuccessRate = (double)_totalSuccessfulRequests / _totalRequestsSent * 100;
                    }
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

        private void AddLogEntry(string message, int machineId = 0, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string prefix = (machineId == 0)
                ? message
                : $"[机器 {machineId,2}] {message}";

            string fullMessage = $"[{timestamp}] {prefix}";

            _ = WriteLogToFile(fullMessage, isError);
            _totalLogCache.Enqueue(fullMessage);

            if (machineId > 0 && _machineLogCaches.ContainsKey(machineId))
            {
                _machineLogCaches[machineId].Enqueue(fullMessage);
            }
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

        // 修复：FindVisualChild 明确返回 ListBox（泛型签名改为具体类型，或调用时指定 ListBox）
        private ListBox FindVisualChild<ListBox>(DependencyObject parent, string childName) where ListBox : DependencyObject
        {
            if (parent == null) return null;

            ListBox foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                ListBox childType = child as ListBox;
                if (childType == null)
                {
                    foundChild = FindVisualChild<ListBox>(child, childName);
                    if (foundChild != null) break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (ListBox)child;
                        break;
                    }
                    else
                    {
                        foundChild = FindVisualChild<ListBox>(child, childName);
                        if (foundChild != null) break;
                    }
                }
                else
                {
                    foundChild = (ListBox)child;
                    break;
                }
            }
            return foundChild;
        }

        private void LoadHistory()
        {
            try
            {
                string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PollingHistory.config");
                if (File.Exists(historyPath))
                {
                    string content = ReadFileContentWithRetry(historyPath);
                    var historyData = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                    if (historyData != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (historyData.ContainsKey("Endpoint"))
                                txtEndpoint.Text = historyData["Endpoint"];
                            int machineCount = 1;
                            if (historyData.ContainsKey("MachineCount") && int.TryParse(historyData["MachineCount"], out machineCount))
                                txtMachineCount.Value = machineCount;
                            int reqPerSec = 200;
                            if (historyData.ContainsKey("MaxRequestsPerSecond") && int.TryParse(historyData["MaxRequestsPerSecond"], out reqPerSec))
                                txtPollInterval.Value = reqPerSec;
                            if (historyData.ContainsKey("HttpMethod") && historyData["HttpMethod"] == "Get")
                                rbGet.IsChecked = true;
                            else
                                rbPost.IsChecked = true;
                            if (historyData.ContainsKey("ParametersJson"))
                                txtParametersJson.Text = historyData["ParametersJson"];
                        });
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
        #endregion

        #region INotifyPropertyChanged 实现（保持原有不变）
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

        private void LbHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}