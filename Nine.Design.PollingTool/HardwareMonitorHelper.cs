using System;
using System.Text;
using System.Management;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;

/// <summary>
/// 硬件信息监控工具类：获取 CPU/内存 详细信息（兼容 C# 7.3 和 .NET Framework 4.6 及以上版本）
/// </summary>
public static class HardwareMonitorHelper
{
    #region 兼容 C# 7.3 的轻量级数据结构体（替换值元组，无兼容性问题）
    /// <summary>
    /// CPU 核心/逻辑数结构体（承载物理核心数、逻辑线程数）
    /// </summary>
    private struct CpuCoreLogicalCount
    {
        /// <summary>
        /// 物理核心数
        /// </summary>
        public uint CoreCount { get; set; }

        /// <summary>
        /// 逻辑线程数
        /// </summary>
        public uint LogicalCount { get; set; }
    }

    /// <summary>
    /// 内存 占用率/可用/总量结构体（承载占用率%、可用GB、总量GB）
    /// </summary>
    private struct MemoryUsedAvailableTotal
    {
        /// <summary>
        /// 内存占用率（%，0-100）
        /// </summary>
        public double UsagePercent { get; set; }

        /// <summary>
        /// 可用内存（GB）
        /// </summary>
        public double AvailableGb { get; set; }

        /// <summary>
        /// 总内存（GB）
        /// </summary>
        public double TotalGb { get; set; }
    }

    /// <summary>
    /// 硬件面板格式化数据模型（用于UI直接绑定显示，兼容 C# 7.3）
    /// 格式：CPU=占用%/核心数/逻辑数；内存=占用率%/可用GB/总量GB
    /// </summary>
    public class HardwarePanelStatus
    {
        /// <summary>
        /// CPU格式化文本（占用%/核心数/逻辑数）
        /// </summary>
        public string CpuStatusText { get; set; } = "0.0%/0核/0线程";

        /// <summary>
        /// 内存格式化文本（占用率%/可用GB/总量GB，修正后格式）
        /// </summary>
        public string MemoryStatusText { get; set; } = "0.0%/0.0GB/0.0GB";

        /// <summary>
        /// 容错标记（是否解析失败）
        /// </summary>
        public bool IsError { get; set; } = false;

        /// <summary>
        /// 错误信息（解析失败时返回）
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }
    #endregion

    #region Windows API 声明（兼容 C# 7.3 和 .NET Framework 4.6）
    /// <summary>
    /// Windows API 结构体：用于获取内存信息（兼容 C# 7.3 及以下版本）
    /// 移除无参数构造函数，改为手动初始化 dwLength
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength; // 结构体大小（必须手动初始化）
        public uint dwMemoryLoad; // 内存占用率（0-100，API 直接返回，更准确）
        public ulong ullTotalPhys; // 物理内存总量（字节）
        public ulong ullAvailPhys; // 可用物理内存（字节）
        public ulong ullTotalPageFile; // 页面文件总量（字节）
        public ulong ullAvailPageFile; // 可用页面文件（字节）
        public ulong ullTotalVirtual; // 虚拟内存总量（字节）
        public ulong ullAvailVirtual; // 可用虚拟内存（字节）
        public ulong ullAvailExtendedVirtual; // 扩展虚拟内存（保留）
    }

    /// <summary>
    /// Windows API：获取全局内存状态（兼容 C# 7.3 和 .NET Framework 4.6）
    /// </summary>
    /// <param name="lpBuffer">内存信息结构体</param>
    /// <returns>是否调用成功</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    #endregion

    #region CPU 相关信息查询（无修改，保持原有功能）
    /// <summary>
    /// 获取 CPU 基本信息（核心数、逻辑数、基础频率）
    /// </summary>
    /// <returns>CPU 基本信息字符串</returns>
    public static string GetCpuBasicInfo()
    {
        try
        {
            ManagementObjectSearcher cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            StringBuilder cpuBasicInfo = new StringBuilder();

            foreach (ManagementObject mo in cpuSearcher.Get())
            {
                string cpuName = mo["Name"]?.ToString() ?? "未知CPU";
                uint coreCount = (uint)(mo["NumberOfCores"] ?? 0);
                uint logicalCount = (uint)(mo["NumberOfLogicalProcessors"] ?? 0);
                uint maxClockSpeed = (uint)(mo["MaxClockSpeed"] ?? 0);
                double maxClockSpeedGhz = Math.Round(maxClockSpeed / 1000.0, 2);

                cpuBasicInfo.AppendLine($"CPU 名称：{cpuName}");
                cpuBasicInfo.AppendLine($"物理核心数：{coreCount} 核");
                cpuBasicInfo.AppendLine($"逻辑处理器数：{logicalCount} 线程");
                cpuBasicInfo.AppendLine($"基础频率：{maxClockSpeedGhz} GHz（{maxClockSpeed} MHz）");
                break;
            }

            return cpuBasicInfo.ToString();
        }
        catch (Exception ex)
        {
            return $"获取 CPU 基本信息失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取 CPU 实时占用率（%）
    /// </summary>
    /// <returns>CPU 占用率（保留1位小数）</returns>
    public static string GetCpuUsage()
    {
        try
        {
            using (PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
            {
                cpuCounter.NextValue();
                Thread.Sleep(100);
                float cpuUsage = cpuCounter.NextValue();
                return $"{Math.Round(cpuUsage, 1)}%";
            }
        }
        catch (Exception ex)
        {
            return $"获取 CPU 占用率失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取 CPU 实时频率（近似值，基于 WMI 动态查询）
    /// </summary>
    /// <returns>CPU 实时频率字符串</returns>
    public static string GetCpuCurrentFrequency()
    {
        try
        {
            ManagementObjectSearcher cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject mo in cpuSearcher.Get())
            {
                uint currentClockSpeed = (uint)(mo["CurrentClockSpeed"] ?? 0);
                if (currentClockSpeed == 0)
                {
                    return "无法获取实时频率（CPU不支持）";
                }
                double currentClockSpeedGhz = Math.Round(currentClockSpeed / 1000.0, 2);
                return $"{currentClockSpeedGhz} GHz（{currentClockSpeed} MHz）";
            }
            return "获取 CPU 实时频率失败";
        }
        catch (Exception ex)
        {
            return $"获取 CPU 实时频率失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取 CPU 核心数和逻辑数（返回自定义结构体，无冗余信息，用于UI面板）
    /// 兼容 C# 7.3 和 .NET Framework 4.6，不使用值元组
    /// </summary>
    /// <returns>CPU 核心/逻辑数结构体</returns>
    private static CpuCoreLogicalCount GetCpuCoreAndLogicalCount()
    {
        CpuCoreLogicalCount cpuCount = new CpuCoreLogicalCount();

        try
        {
            ManagementObjectSearcher cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject mo in cpuSearcher.Get())
            {
                cpuCount.CoreCount = (uint)(mo["NumberOfCores"] ?? 0);
                cpuCount.LogicalCount = (uint)(mo["NumberOfLogicalProcessors"] ?? 0);
                mo.Dispose();
                break;
            }
        }
        catch (Exception)
        {
            // 容错：保留默认值 0
        }

        return cpuCount;
    }
    #endregion

    #region 内存 相关信息查询（核心修正：内存格式改为 占用率%/可用GB/总量GB）
    /// <summary>
    /// 获取内存总信息（总量、占用率、内存条数、单条频率）
    /// 兼容 C# 7.3 和 .NET Framework 4.6，不依赖 ComputerInfo
    /// </summary>
    /// <returns>内存完整信息字符串</returns>
    public static string GetMemoryFullInfo()
    {
        try
        {
            StringBuilder memoryInfo = new StringBuilder();

            MEMORYSTATUSEX memoryStatus = new MEMORYSTATUSEX();
            memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memoryStatus))
            {
                memoryInfo.AppendLine("获取内存总量/占用率失败：Windows API 调用失败");
            }
            else
            {
                // 内存总量（字节转换为 GB，保留2位小数）
                ulong totalMemoryBytes = memoryStatus.ullTotalPhys;
                double totalMemoryGb = Math.Round(totalMemoryBytes / 1024.0 / 1024.0 / 1024.0, 2);
                // 可用内存（字节转换为 GB）
                ulong availableMemoryBytes = memoryStatus.ullAvailPhys;
                double availableMemoryGb = Math.Round(availableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 2);
                // 内存占用率（%，API 直接返回 0-100，无需手动计算）
                double memoryUsagePercent = memoryStatus.dwMemoryLoad;

                // 拼接内存总量/占用率信息
                memoryInfo.AppendLine($"物理内存总量：{totalMemoryGb} GB（{totalMemoryBytes / 1024 / 1024 / 1024} GB 整数）");
                memoryInfo.AppendLine($"可用内存：{availableMemoryGb} GB");
                memoryInfo.AppendLine($"内存占用率：{memoryUsagePercent} %");
            }

            // 2. 获取 内存条数 和 单条频率（WMI 查询硬件信息，逻辑不变）
            ManagementObjectSearcher memorySearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            uint memoryCount = 0; // 内存条数
            StringBuilder memorySpeedInfo = new StringBuilder(); // 单条频率信息

            foreach (ManagementObject mo in memorySearcher.Get())
            {
                memoryCount++;
                // 内存容量（MB）
                uint capacityMb = (uint)(mo["Capacity"] != null ? (ulong)mo["Capacity"] / 1024 / 1024 : 0);
                double capacityGb = Math.Round(capacityMb / 1024.0, 2);
                // 内存频率（MHz）
                uint speed = (uint)(mo["Speed"] ?? 0);
                // 内存型号
                string partNumber = mo["PartNumber"]?.ToString() ?? "未知型号";

                memorySpeedInfo.AppendLine($"  第 {memoryCount} 条：{capacityGb} GB | 频率 {speed} MHz | 型号 {partNumber}");
            }

            // 3. 拼接内存条数/详情信息
            memoryInfo.AppendLine($"内存条数：{memoryCount} 条");
            if (memoryCount > 0)
            {
                memoryInfo.AppendLine("单条内存详情：");
                memoryInfo.AppendLine(memorySpeedInfo.ToString());
            }
            else
            {
                memoryInfo.AppendLine("单条内存详情：无法获取（无WMI权限或硬件不支持）");
            }

            return memoryInfo.ToString();
        }
        catch (Exception ex)
        {
            return $"获取内存信息失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 获取内存 占用率%/可用GB/总量GB（返回自定义结构体，无冗余信息，用于UI面板）
    /// 兼容 C# 7.3 和 .NET Framework 4.6，不使用值元组
    /// </summary>
    /// <returns>内存 占用率/可用/总量结构体</returns>
    private static MemoryUsedAvailableTotal GetMemoryUsedAvailableTotal()
    {
        // 初始化结构体（默认值 0.0）
        MemoryUsedAvailableTotal memoryData = new MemoryUsedAvailableTotal();

        try
        {
            MEMORYSTATUSEX memoryStatus = new MEMORYSTATUSEX();
            memoryStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memoryStatus))
            {
                // 1. 内存占用率（%，API 直接返回 0-100，保留1位小数）
                memoryData.UsagePercent = Math.Round((double)memoryStatus.dwMemoryLoad, 1);
                // 2. 总内存（GB，保留1位小数）
                memoryData.TotalGb = Math.Round(memoryStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
                // 3. 可用内存（GB，保留1位小数）
                memoryData.AvailableGb = Math.Round(memoryStatus.ullAvailPhys / 1024.0 / 1024.0 / 1024.0, 1);
            }
        }
        catch (Exception)
        {
            // 容错：保留默认值 0.0
        }

        return memoryData;
    }
    #endregion

    #region 拼接完整统计信息（用于轮询输出）
    /// <summary>
    /// 拼接 CPU + 内存 完整统计信息（直接用于轮询展示，兼容 C# 7.3）
    /// </summary>
    /// <returns>完整硬件统计信息字符串</returns>
    public static string GetFullHardwareStatisticInfo()
    {
        StringBuilder fullInfo = new StringBuilder();
        fullInfo.AppendLine("==================== 硬件统计信息 ====================");
        fullInfo.AppendLine($"查询时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        fullInfo.AppendLine();
        fullInfo.AppendLine("---------- CPU 信息 ----------");
        fullInfo.AppendLine(GetCpuBasicInfo());
        fullInfo.AppendLine($"CPU 实时占用率：{GetCpuUsage()}");
        fullInfo.AppendLine($"CPU 实时频率：{GetCpuCurrentFrequency()}");
        fullInfo.AppendLine();
        fullInfo.AppendLine("---------- 内存 信息 ----------");
        fullInfo.AppendLine(GetMemoryFullInfo());
        fullInfo.AppendLine("======================================================");
        fullInfo.AppendLine();
        fullInfo.AppendLine();

        return fullInfo.ToString();
    }
    #endregion

    #region 获取 UI 面板所需完整格式化数据（修正内存格式，兼容 C# 7.3）
    /// <summary>
    /// 获取 UI 面板所需完整格式化数据（CPU/内存一键返回，兼容 C# 7.3 和 .NET Framework 4.6）
    /// 格式：CPU=占用%/核心数/逻辑数；内存=占用率%/可用GB/总量GB
    /// </summary>
    /// <returns>硬件面板格式化数据模型</returns>
    public static HardwarePanelStatus GetFullHardwarePanelStatus()
    {
        // 初始化返回结果（默认容错值）
        HardwarePanelStatus panelStatus = new HardwarePanelStatus();

        try
        {
            // ===== 1. 构建 CPU 面板文本（逻辑不变） =====
            string cpuUsage = GetCpuUsage();
            if (cpuUsage.Contains("失败"))
            {
                throw new Exception($"CPU 占用率获取失败：{cpuUsage}");
            }
            CpuCoreLogicalCount cpuCount = GetCpuCoreAndLogicalCount();
            panelStatus.CpuStatusText = $"{cpuUsage}/{cpuCount.CoreCount}核/{cpuCount.LogicalCount}线程";

            // ===== 2. 构建 内存 面板文本（核心修正：格式改为 占用率%/可用GB/总量GB） =====
            MemoryUsedAvailableTotal memoryData = GetMemoryUsedAvailableTotal();
            // 校验内存数据（避免无效值）
            if (memoryData.TotalGb <= 0.0)
            {
                throw new Exception("内存总量获取失败，返回无效值");
            }
            // 拼接 内存 面板文本（新格式：占用率%/可用GB/总量GB）
            panelStatus.MemoryStatusText = $"{memoryData.UsagePercent}%/{memoryData.AvailableGb}GB/{memoryData.TotalGb}GB";

            // ===== 3. 标记解析成功 =====
            panelStatus.IsError = false;
            panelStatus.ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            // ===== 4. 容错处理：保留默认值，记录错误信息 =====
            panelStatus.IsError = true;
            panelStatus.ErrorMessage = ex.Message;
        }

        return panelStatus;
    }

    /// <summary>
    /// 综合方法：一键获取 日志文本 + 面板格式化数据（兼容 C# 7.3 + .NET Framework 4.6）
    /// </summary>
    /// <param name="hardwareLogInfo">输出参数：完整硬件日志文本</param>
    /// <returns>硬件面板格式化数据模型</returns>
    public static HardwarePanelStatus GetFullHardwareData(out string hardwareLogInfo)
    {
        // 初始化输出参数
        hardwareLogInfo = string.Empty;

        try
        {
            // 获取日志文本
            hardwareLogInfo = GetFullHardwareStatisticInfo();
            // 获取面板数据
            return GetFullHardwarePanelStatus();
        }
        catch (Exception ex)
        {
            hardwareLogInfo = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 硬件数据获取失败：{ex.Message}";
            return new HardwarePanelStatus
            {
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
    #endregion
}