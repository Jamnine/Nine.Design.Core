using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Nine.Design.PollingTool
{
    /// <summary>
    /// 固定日志类型枚举（保留标识，不与动态机器ID冲突）
    /// </summary>
    public enum FixedLogType
    {
        /// <summary>
        /// 全部日志（默认）
        /// </summary>
        All = 0,

        /// <summary>
        /// 硬件监控日志（CPU、内存等，固定标识）
        /// </summary>
        HardwareMonitor = 1,

        /// <summary>
        /// 系统日志（程序启动、配置变更等，固定标识）
        /// </summary>
        System = 2,

        /// <summary>
        /// 业务机器日志（动态机器请求，后续过滤可排除）
        /// </summary>
        BusinessMachine = 3,

        /// <summary>
        /// 业务机器日志（动态机器请求，后续过滤可排除）
        /// </summary>
        PortMonitor = 4
    }


    public enum TestMode
    {
        HighFrequency,  // 高频压测模式（一秒几次）
        StableMonitor   // 稳定监控模式（几秒一次）
    }

    /// <summary>
    /// 日志条目类（扩展：新增固定日志类型）
    /// </summary>
    public class LogEntry
    {
        public string Message { get; set; }
        public int MachineId { get; set; }
        public bool IsError { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 新增：固定日志类型（用于过滤：硬件、系统、业务）
        /// </summary>
        public FixedLogType LogType { get; set; }
    }

    public class HistoryItem
    {
        // 1. 新增：唯一标识（Guid），用于精准匹配删除/编辑，保存时自动生成
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid(); // 实例化时自动生成唯一Guid

        // 2. 新增：用户自定义名称（可编辑）
        [JsonProperty("customName")]
        public string CustomName { get; set; } = string.Empty;

        // 原有字段（保留不变）
        [JsonProperty("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [JsonProperty("machineCount")]
        public string MachineCount { get; set; } = string.Empty;

        [JsonProperty("pollInterval")]
        public string PollInterval { get; set; } = string.Empty;

        [JsonProperty("parametersJson")]
        public string ParametersJson { get; set; } = string.Empty;

        [JsonProperty("httpMethod")]
        public string HttpMethod { get; set; } = string.Empty;

        [JsonProperty("testMode")]
        public string TestMode { get; set; } = string.Empty;

        [JsonProperty("requestConfigValue")]
        public string RequestConfigValue { get; set; } = string.Empty;

        [JsonProperty("saved_time")]
        public DateTime SavedTime { get; set; } = DateTime.Now;
    }

    // 历史记录集合模型
    public class HistoryRoot
    {
        [JsonProperty("history")]
        public List<HistoryItem> HistoryList { get; set; }

        public HistoryRoot()
        {
            HistoryList = new List<HistoryItem>();
        }
    }

    // 机器数据模型（统计信息）
    public class MachineDataModel : INotifyPropertyChanged
    {
        private int _machineId;
        private string _tabTitle;
        private int _totalRequests;
        private int _successRequests;
        private Dictionary<string, int> _errorStats;
        private Tuple<double, double> _responseTimeExtremes;
        private DateTime _lastRequestTime;
        private bool _isPolling;

        public int MachineId
        {
            get => _machineId;
            set
            {
                _machineId = value;
                OnPropertyChanged(nameof(MachineId));
            }
        }

        public string TabTitle
        {
            get => _tabTitle;
            set
            {
                _tabTitle = value;
                OnPropertyChanged(nameof(TabTitle));
            }
        }

        public int TotalRequests
        {
            get => _totalRequests;
            set
            {
                _totalRequests = value;
                OnPropertyChanged(nameof(TotalRequests));
            }
        }

        public int SuccessRequests
        {
            get => _successRequests;
            set
            {
                _successRequests = value;
                OnPropertyChanged(nameof(SuccessRequests));
            }
        }

        public Dictionary<string, int> ErrorStats
        {
            get => _errorStats;
            set
            {
                _errorStats = value;
                OnPropertyChanged(nameof(ErrorStats));
            }
        }

        public Tuple<double, double> ResponseTimeExtremes
        {
            get => _responseTimeExtremes;
            set
            {
                _responseTimeExtremes = value;
                OnPropertyChanged(nameof(ResponseTimeExtremes));
            }
        }

        public DateTime LastRequestTime
        {
            get => _lastRequestTime;
            set
            {
                _lastRequestTime = value;
                OnPropertyChanged(nameof(LastRequestTime));
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Tab页面项模型（绑定TabControl）
    public class MachineTabItemModel : INotifyPropertyChanged
    {
        private string _tabTitle;
        private int _machineId;
        private MachineDataModel _machineData;

        public string TabTitle
        {
            get => _tabTitle;
            set
            {
                _tabTitle = value;
                OnPropertyChanged(nameof(TabTitle));
            }
        }

        public int MachineId
        {
            get => _machineId;
            set
            {
                _machineId = value;
                OnPropertyChanged(nameof(MachineId));
            }
        }

        public MachineDataModel MachineData
        {
            get => _machineData;
            set
            {
                _machineData = value;
                OnPropertyChanged(nameof(MachineData));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}