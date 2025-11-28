using Nine.Design.Login.Models;
using Nine.Design.Login.Views;

namespace Nine.Design.Core
{
    public class LoginHandler
    {
        public void ShowLoginWindow()
        {
            // 创建登录窗口（DLL）
            var loginWindow = new FrmLogin();

            // 订阅登录提交事件
            loginWindow.OnLoginSubmit += LoginWindow_OnLoginSubmit;

            // 显示登录窗口
            loginWindow.ShowDialog();
        }

        // 事件回调（.NET 4.5 支持 async void 用于事件）
        private async Task LoginWindow_OnLoginSubmit(object sender, LoginInputEventArgs e)
        {
            // 1. 调用登录逻辑（返回 Tuple<bool, string>）
            Tuple<bool, string> loginResult = await VerifyLoginAsync(e.Username, e.Password);

            // 2. 通过 Item1/Item2 获取结果（.NET 4.5 兼容方式）
            bool success = loginResult.Item1;
            string message = loginResult.Item2;

            // 3. 回传结果给DLL
            e.LoginSuccess = success;
            e.Message = message;

            // 4. 保存密码（如果需要）
            if (success && e.RememberPassword)
            {
                SaveEncryptedPassword(e.Username, e.Password);
            }

        }

        // 登录核心逻辑（返回 Tuple，避免元组解构）
        private async Task<Tuple<bool, string>> VerifyLoginAsync(string username, string password)
        {
            try
            {
                // 模拟API调用（.NET 4.5 中可使用 HttpClient）
                await Task.Delay(1000); // 模拟网络延迟

                // 验证逻辑
                if (username == "999999" && password == "123")
                {

                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();
                    return Tuple.Create(true, "登录成功"); // 返回成功结果
                }
                else
                {
                    return Tuple.Create(false, "用户名或密码错误"); // 返回失败结果
                }
            }
            catch (Exception ex)
            {
                return Tuple.Create(false, $"登录失败：{ex.Message}"); // 异常处理
            }
        }

        // 密码加密存储
        private void SaveEncryptedPassword(string username, string password)
        {
            // 示例：加密并存储（实际项目需实现加密逻辑）
            // var encryptedPassword = Encrypt(password);
            // 写入配置文件或数据库...
        }
    }
}
