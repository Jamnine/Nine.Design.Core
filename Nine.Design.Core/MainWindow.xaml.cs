using Panuon.WPF.UI;
using System.Windows;

namespace Nine.Design.Core
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : WindowX
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        #region 关闭程序
        private void btn_Exit_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 1. 手动释放自定义资源（根据你的项目实际情况添加，示例如下）
            ReleaseCustomResources();

            // 2. 关闭当前窗口（可选，因为 Application.Shutdown() 会关闭所有窗口）
            this.Close();

            // 3. 终止整个应用程序（关键：关闭所有窗口、主线程，释放大部分系统资源）
            Application.Current.Shutdown();

            // 4. 强制终止进程（兜底方案：确保进程100%退出，避免极端情况残留）
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        /// <summary>
        /// 释放项目中的自定义资源（根据实际使用场景修改）
        /// </summary>
        private void ReleaseCustomResources()
        {
            //// 示例1：关闭数据库连接（若使用了数据库）
            //if (myDbConnection != null && myDbConnection.State == System.Data.ConnectionState.Open)
            //{
            //    myDbConnection.Close();
            //    myDbConnection.Dispose();
            //}

            //// 示例2：停止后台线程（若有后台任务，必须先停止，否则线程会让进程驻留）
            //if (myBackgroundThread != null && myBackgroundThread.IsAlive)
            //{
            //    // 假设用 _cancellationTokenSource 控制线程退出
            //    _cancellationTokenSource?.Cancel();
            //    myBackgroundThread.Join(); // 等待线程执行完退出逻辑
            //    myBackgroundThread = null;
            //}

            //// 示例3：释放文件句柄（若打开了文件、流）
            //myFileStream?.Close();
            //myFileStream?.Dispose();

            //// 示例4：释放第三方控件/资源（如ActiveX控件、网络连接等）
            //thirdPartyControl?.Dispose();
            //networkClient?.Disconnect();
        }
        #endregion
    }
}