using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Nine.Design.PollingTool
{
    /// <summary>
    /// 单机器数据模型（存储日志、统计、响应时间等）
    /// </summary>
    public class MachineDataModel
    {
        /// <summary>
        /// 机器ID
        /// </summary>
        public int MachineId { get; set; }

        /// <summary>
        /// 机器是否正在轮询
        /// </summary>
        public bool IsPolling { get; set; }

        /// <summary>
        /// 机器专属日志缓存
        /// </summary>
        public ConcurrentQueue<string> MachineLogCache { get; set; } = new ConcurrentQueue<string>();

        /// <summary>
        /// 总请求数
        /// </summary>
        public int TotalRequests { get; set; } = 0;

        /// <summary>
        /// 成功请求数
        /// </summary>
        public int SuccessRequests { get; set; } = 0;

        /// <summary>
        /// 错误统计
        /// </summary>
        public Dictionary<string, int> ErrorStats { get; set; } = new Dictionary<string, int>
        {
            { "超时", 0 }, { "网络错误", 0 }, { "HTTP错误", 0 },
            { "参数错误", 0 }, { "其他错误", 0 }, { "成功次数", 0 },
            { "端口超限", 0 }, { "频率限制", 0 }
        };

        /// <summary>
        /// 响应时间极值（最小、最大，单位：毫秒）
        /// </summary>
        public Tuple<double, double> ResponseTimeExtremes { get; set; } = new Tuple<double, double>(double.MaxValue, double.MinValue);

        /// <summary>
        /// 最后请求时间（用于频率控制）
        /// </summary>
        public DateTime LastRequestTime { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// 页面标签模型（用于TabControl页面切换）
    /// </summary>
    public class MachineTabItemModel
    {
        /// <summary>
        /// 页面标题
        /// </summary>
        public string TabTitle { get; set; }

        /// <summary>
        /// 对应机器ID（0=总览页面）
        /// </summary>
        public int MachineId { get; set; }

        /// <summary>
        /// 页面内容（日志+统计，后续绑定）
        /// </summary>
        public MachineDataModel MachineData { get; set; }
    }
}