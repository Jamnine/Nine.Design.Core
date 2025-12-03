using Nine.Design.Core.Helpers;
using System;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nine.Design.Core
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 应用当前版本（从 app.config 读取，传给更新程序）
        public static string CurrentVersion { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 从 app.config 读取版本号
            InitCurrentVersion();

            // 2. 注册全局异常处理
            RegisterGlobalExceptionHandlers();

            // 3. 启用硬件加速
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            // 4. 根据 IsEnableUpdate 控制是否执行更新检查（核心开关）
            bool isEnableUpdate = GetConfigValue<bool>("IsEnableUpdate", false);
            if (isEnableUpdate)
            {
                Logger.Info("已启用自动更新，开始执行更新检查");
                CheckUpdateSync(); // 执行更新检查（阻塞，不启动登录）
            }
            else
            {
                Logger.Info("已禁用自动更新，直接启动应用");
                StartLoginWindow(); // 直接启动登录窗口
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info($"应用退出。累计日志数：{Logger.WrittenCount}，失败日志数：{Logger.FailedCount}");
            base.OnExit(e);
        }

        #region 核心初始化：从 app.config 读取版本号
        /// <summary>
        /// 从 app.config 读取当前版本（传给更新程序）
        /// </summary>
        private void InitCurrentVersion()
        {
            try
            {
                CurrentVersion = ConfigurationManager.AppSettings["Version"];
                if (string.IsNullOrWhiteSpace(CurrentVersion))
                {
                    Logger.Info("app.config 中 Version 为空，使用默认版本：1.0");
                    CurrentVersion = "1.0";
                }
                Logger.Info($"从 app.config 读取版本号：{CurrentVersion}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "读取版本号失败");
                MessageBox.Show("读取应用版本号失败，程序将退出！", "致命错误", MessageBoxButton.OK, MessageBoxImage.Stop);
                Environment.Exit(1);
            }
        }
        #endregion

        #region 辅助方法：安全读取 app.config 配置（避免配置缺失报错）
        /// <summary>
        /// 从 app.config 读取配置，支持默认值，避免配置缺失抛异常
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键名</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值（配置缺失则返回默认值）</returns>
        private T GetConfigValue<T>(string key, T defaultValue)
        {
            try
            {
                string configValue = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(configValue))
                {
                    Logger.Info($"app.config 中未配置 {key}，使用默认值：{defaultValue}");
                    return defaultValue;
                }
                // 类型转换（支持 bool、string、int 等常见类型）
                return (T)Convert.ChangeType(configValue, typeof(T));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"读取配置 {key} 失败");
                return defaultValue;
            }
        }
        #endregion

        #region 全局异常处理
        private void RegisterGlobalExceptionHandlers()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            Logger.Info("全局异常处理已注册");
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "UI线程发生未处理异常");
            MessageBox.Show($"程序发生UI错误：{e.Exception.Message}\n详细信息已记录到日志文件", "应用错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Logger.Error(exception, "非UI线程发生严重未处理异常");
            MessageBox.Show($"程序发生严重错误：{exception?.Message ?? "未知错误"}\n程序将退出", "致命错误", MessageBoxButton.OK, MessageBoxImage.Stop);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "Task发生未观察到的异常");
            e.SetObserved();
        }
        #endregion

        #region 更新检查
        /// <summary>
        /// 同步执行更新检查
        /// </summary>
        private void CheckUpdateSync()
        {
            try
            {
                // 从 app.config 读取所有更新相关配置（核心修改：用 ConfigurationManager）
                string updateUrl = GetConfigValue<string>("UpdateAddress", string.Empty);
                bool isManualUpdate = GetConfigValue<bool>("Auto", true); 
                string theme = GetConfigValue<string>("Theme", "Pro");
                bool consoleTesting = GetConfigValue<bool>("ConsoleTesting", false);

                // 校验关键配置：更新地址不能为空
                if (string.IsNullOrWhiteSpace(updateUrl))
                {
                    Logger.Error("app.config 中 UpdateAddress（更新地址）为空，更新检查失败");
                    MessageBox.Show("更新地址未配置，无法执行更新检查！", "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StartLoginWindow(); // 配置缺失仍启动登录，不影响使用
                    return;
                }

                Logger.Info($"开始调用更新程序 - 当前版本：{CurrentVersion}，更新地址：{updateUrl}，手动更新模式：{isManualUpdate}，主题：{theme}");

                // 调用更新程序（阻塞执行，版本对比/下载/更新全由更新程序处理）
                // 参数顺序和类型必须与更新程序的 CheckUpdateStatus 方法完全匹配！
                Nine.Design.Updater.Updater.CheckUpdateStatus(
                    urlAddress: updateUrl,              // 更新地址
                    version: CurrentVersion,            // 当前版本（从 app.config 读取）
                    configName: "Nine.Design.Core.dll", // 目标文件（Core版本传.dll，Framework传.exe）
                    Auto: isManualUpdate,               // 是否手动更新（按更新程序定义）
                    ConsoleTesting: consoleTesting,     // 调试模式
                    Theme: theme                        // 主题
                );

                // 执行到这里说明：1. 无新版本；2. 用户取消更新 → 启动登录窗口
                Logger.Info("更新程序处理完成（无新版本/用户取消），启动登录窗口");
                StartLoginWindow();
            }
            catch (OperationCanceledException)
            {
                // 用户取消更新 → 启动登录
                Logger.Info("用户取消更新，启动登录窗口");
                StartLoginWindow();
            }
            catch (Exception ex)
            {
                // 输出完整异常信息（含内部异常），方便排查更新程序调用失败原因
                Logger.Error(ex.ToString(), "更新程序调用异常（含内部异常）");
                MessageBox.Show($"更新程序调用失败：{ex.Message}\n内部错误：{ex.InnerException?.Message}", "更新调用异常", MessageBoxButton.OK, MessageBoxImage.Error);
                StartLoginWindow(); // 异常仍启动登录，不影响旧版本使用
            }
        }
        #endregion

        #region 统一启动登录窗口（避免代码冗余）
        /// <summary>
        /// 启动登录窗口（所有场景统一调用）
        /// </summary>
        private void StartLoginWindow()
        {
            var login = new LoginHandler();
            login.ShowLoginWindow();
            Logger.Info($"登录窗口已启动，当前应用版本：{CurrentVersion}");
        }
        #endregion

        #region 窗口关闭逻辑（供更新程序调用，释放文件占用）
        /// <summary>
        /// 关闭所有打开的窗口（释放文件占用，更新程序需时可调用）
        /// 若更新程序已内置此逻辑，可删除
        /// </summary>
        public void CloseAllWindows()
        {
            try
            {
                Logger.Info("开始关闭所有打开的窗口（释放文件占用）");
                Dispatcher.Invoke(() =>
                {
                    foreach (var window in Current.Windows.Cast<Window>().ToList())
                    {
                        Logger.Info($"正在关闭窗口：{window.Title ?? "未命名窗口"}");
                        window.Close();
                    }
                    MainWindow = null;
                });

                Thread.Sleep(1500);
                Logger.Info("所有窗口关闭完成，文件资源已释放");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "关闭窗口时发生异常");
                throw;
            }
        }
        #endregion
    }
}