using System.IO;

namespace Nine.Design.Core.Helpers
{
    public static class Logger
    {
        // 线程安全锁
        private static readonly ReaderWriterLockSlim LogWriteLock = new ReaderWriterLockSlim();

        // 日志根目录 (在 Debug 目录下)
        private static readonly string _logRootDirectory;

        // 日志文件大小限制 (例如：1MB)
        private const long MaxFileSizeBytes = 1 * 1024 * 1024; // 10,485,760 bytes

        // 日志文件保留天数
        private const int LogRetentionDays = 30;

        // 统计信息
        public static int WrittenCount { get; private set; }
        public static int FailedCount { get; private set; }

        static Logger()
        {
            // 在类静态构造函数中初始化日志目录
            // 确保日志目录在应用程序的输出目录 (bin/Debug) 下
            _logRootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            try
            {
                if (!Directory.Exists(_logRootDirectory))
                {
                    Directory.CreateDirectory(_logRootDirectory);
                }
                // 初始化时清理一次旧日志
                DeleteOldLogFiles();
            }
            catch (Exception ex)
            {
                // 如果创建目录失败，记录到控制台并允许程序继续运行
                Console.WriteLine($"[Logger Initialization Error] Failed to create or clean log directory: {ex.Message}");
                FailedCount++;
            }
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        public static void Info(string message, params object[] args) => Log(LogLevel.Info, message, args);

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public static void Error(string message, params object[] args) => Log(LogLevel.Error, message, args);

        /// <summary>
        /// 记录异常日志
        /// </summary>
        public static void Error(Exception ex, string message = null, params object[] args)
        {
            string logMessage = message != null ? string.Format(message, args) : "An exception occurred.";
            logMessage += $"\nException Details:\n{ex}";
            Log(LogLevel.Error, logMessage);
        }

        /// <summary>
        /// 记录调试日志
        /// </summary>
        public static void Debug(string message, params object[] args) => Log(LogLevel.Debug, message, args);

        /// <summary>
        /// 核心日志写入方法
        /// </summary>
        private static void Log(LogLevel level, string message, params object[] args)
        {
            if (args.Length > 0)
            {
                message = string.Format(message, args);
            }

            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpper()}] - {message}\n";

            string dayFolder = DateTime.Now.ToString("yyyyMMdd");
            string logDirectory = Path.Combine(_logRootDirectory, dayFolder);
            string prefix = "application_";

            try
            {
                LogWriteLock.EnterWriteLock();

                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // 清理旧日志（可以考虑定期执行，而不是每次都执行）
                if (WrittenCount % 100 == 0) // 每写100条日志尝试清理一次，减少IO压力
                {
                    DeleteOldLogFiles();
                }

                string logFilePath = GetNextAvailableLogFile(logDirectory, prefix);

                // 写入日志
                File.AppendAllText(logFilePath, logEntry);
                WrittenCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Error] Failed to write log entry: {ex.Message}");
                FailedCount++;
            }
            finally
            {
                if (LogWriteLock.IsWriteLockHeld)
                {
                    LogWriteLock.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// 获取下一个可用的日志文件路径（考虑大小限制）
        /// </summary>
        private static string GetNextAvailableLogFile(string directory, string prefix)
        {
            // 查找当前目录下所有以 prefix 开头的日志文件
            var files = Directory.GetFiles(directory, $"{prefix}*.log")
                                 .OrderByDescending(f => f)
                                 .ToList();

            // 如果没有文件，创建第一个
            if (!files.Any())
            {
                return Path.Combine(directory, $"{prefix}001.log");
            }

            var lastFile = new FileInfo(files.First());

            // 如果最后一个文件大小未超过限制，则使用它
            if (lastFile.Length < MaxFileSizeBytes)
            {
                return lastFile.FullName;
            }

            // 否则，创建一个新的文件，序号加1
            string lastFileName = Path.GetFileNameWithoutExtension(lastFile.Name);
            if (int.TryParse(lastFileName.Substring(prefix.Length), out int lastIndex))
            {
                string newFileName = $"{prefix}{(lastIndex + 1):D3}.log"; // D3 确保序号是三位数，如 002, 003
                return Path.Combine(directory, newFileName);
            }

            // 如果文件名格式不正确，也创建一个新的文件
            return Path.Combine(directory, $"{prefix}001.log");
        }

        /// <summary>
        /// 删除旧的日志文件
        /// </summary>
        private static void DeleteOldLogFiles()
        {
            if (!Directory.Exists(_logRootDirectory))
                return;

            try
            {
                var cutoffDate = DateTime.Now.AddDays(-LogRetentionDays);

                foreach (var directory in Directory.GetDirectories(_logRootDirectory))
                {
                    string dirName = Path.GetFileName(directory);
                    if (DateTime.TryParseExact(dirName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dirDate))
                    {
                        if (dirDate < cutoffDate)
                        {
                            Directory.Delete(directory, recursive: true);
                            Console.WriteLine($"[Logger Cleanup] Deleted old log directory: {directory}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger Cleanup Error] Failed to delete old log files: {ex.Message}");
                // 此处不增加 FailedCount，因为清理失败不影响主功能
            }
        }
    }

    public enum LogLevel
    {
        Info,
        Error,
        Debug
    }
}