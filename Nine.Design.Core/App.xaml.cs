using Nine.Design.Core.Helpers;
using System.Configuration;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;

namespace Nine.Design.Core
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 构建配置（自动加载 appsettings.json）
            Configuration = new ConfigurationBuilder()
                //.SetBasePath(AppContext.BaseDirectory) // 配置文件所在目录（输出目录）
                .SetBasePath(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build(); // 可选：reloadOnChange=true 支持配置热更新

            // 读取配置
            //string dbConn = Configuration.GetConnectionString("MyDb");
            //int maxRetry = Configuration.GetValue<int>("AppSettings:MaxRetryCount");
            //string logPath = Configuration["AppSettings:LogPath"];

            this.Startup += new StartupEventHandler(App_UpdateStartup);//启动更新程序  

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

        private void App_UpdateStartup(object sender, StartupEventArgs e)
        {
            //修改startup委托更新事件 ，添加 全局异常处理的三个委托
            try
            {
                //重要设置，并非多余实例化一个窗体，目的是作为程序的MainWindow。
                Window mainwindow = new Window();
                CheckUpdate();//检查更新，可以在配置文件更改配置
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            }
            catch (Exception)
            {
                Environment.Exit(0);
            }
        }

        public static void CheckUpdate()
        {
            var config = new ConfigurationBuilder()
                        .SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("appsettings.json")
                        .Build();
            //获取更新地址
            //string UpdateUrl = System.Configuration.ConfigurationManager.AppSettings["UpdateAddress"];
            string UpdateUrl = config.GetValue<string>("AppSettings:UpdateAddress");
            //获取版本号
            //string CustomterVersionNum = System.Configuration.ConfigurationManager.AppSettings["Version"];
            string CustomterVersionNum = config.GetValue<string>("AppSettings:Version");
            //获取是否自动更新
            //bool Auto = Convert.ToBoolean(System.Configuration.ConfigurationManager.AppSettings["Auto"]);
            bool Auto = config.GetValue<bool>("AppSettings:Auto");
            //获取主题
            //string Theme = System.Configuration.ConfigurationManager.AppSettings["Theme"];
            string Theme = config.GetValue<string>("AppSettings:Theme");
            //是否开启测试模式
            //bool ConsoleTesting = Convert.ToBoolean(System.Configuration.ConfigurationManager.AppSettings["ConsoleTesting"]);
            bool ConsoleTesting = config.GetValue<bool>("AppSettings:ConsoleTesting");
            //IsEnableUpdate 是否启用更新 
            if (System.Configuration.ConfigurationManager.AppSettings["IsEnableUpdate"] != null)
            {
                if (Convert.ToBoolean(System.Configuration.ConfigurationManager.AppSettings["IsEnableUpdate"]))
                {
                    Nine.Design.Updater.Updater.CheckUpdateStatus(UpdateUrl, CustomterVersionNum, "Nine.Design.Core.dll", Auto, ConsoleTesting, Theme);

                    //UpdateUrl 更新地址
                    //CustomterVersionNum 版本号
                    //Nine.Design.QTP.NET60.dll 程序名称
                    //Core版本主程序注意后面是【程序名+.dll】Framework版本是【程序名+.exe】
                    //Auto 是否手动更新，true为手动其他为自动
                    //ConsoleTesting 启动调试模式
                }
            }
            return;
        }
    }
}