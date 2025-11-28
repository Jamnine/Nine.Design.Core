using Nine.Design.Core.Helpers;
using System;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 注册全局异常处理事件
            RegisterGlobalExceptionHandlers();

            // 2. 启用硬件加速
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            base.OnStartup(e);

            // 记录应用启动信息
            Logger.Info("Application started successfully.");

            var login = new LoginHandler();
            login.ShowLoginWindow();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 记录应用退出信息
            Logger.Info($"Application exiting. Total logs written: {Logger.WrittenCount}, Failed: {Logger.FailedCount}");
            base.OnExit(e);
        }

        /// <summary>
        /// 注册全局异常处理事件
        /// </summary>
        private void RegisterGlobalExceptionHandlers()
        {
            // 处理 UI 线程上未捕获的异常
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 处理非 UI 线程上未捕获的异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 处理 Task 中未观察到的异常
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // 使用新的 Logger 记录异常
            Logger.Error(e.Exception, "UI 线程发生未处理异常");

            // 向用户显示友好提示
            MessageBox.Show("很抱歉，程序发生错误。详细信息已记录到日志文件中。", "应用程序错误", MessageBoxButton.OK, MessageBoxImage.Error);

            // 设置 e.Handled = true 阻止应用崩溃（在生产环境中根据情况决定）
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                Logger.Error(exception, "非 UI 线程发生严重未处理异常");
            }
            else
            {
                Logger.Error("非 UI 线程发生未知的严重错误");
            }

            // 非 UI 线程的严重异常通常无法恢复，记录日志后让程序自然退出
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "Task 发生未观察到的异常");

            // 标记异常为已观察，防止应用在 GC 时崩溃
            e.SetObserved();
        }
    }
}