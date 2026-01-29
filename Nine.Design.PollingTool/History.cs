using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Nine.Design.PollingTool
{
    // 历史记录项模型
    public class HistoryItem
    {
        public string Endpoint { get; set; }
        public string MachineCount { get; set; }
        public string PollInterval { get; set; }
        public string ParametersJson { get; set; }

        [JsonProperty("saved_time")]
        public DateTime SavedTime { get; set; }
    }

    // 历史记录集合模型
    public class HistoryData
    {
        [JsonProperty("history")]
        public List<HistoryItem> Items { get; set; }

        public HistoryData()
        {
            Items = new List<HistoryItem>();
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