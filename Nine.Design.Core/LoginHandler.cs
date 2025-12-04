using Nine.Design.Core.Http;
using Nine.Design.Core.Model.ViewModels;
using Nine.Design.Login.Models;
using Nine.Design.Login.Views;
using System.Configuration;
using System.Net.Http;

namespace Nine.Design.Core
{
    public class LoginHandler
    {
        private MainWindow _mainWindow;

        /// <summary>
        /// 显示登录窗口（保持原有逻辑）
        /// </summary>
        public void ShowLoginWindow()
        {
            var loginWindow = new FrmLogin();
            loginWindow.OnLoginSubmit += LoginWindow_OnLoginSubmit;

            // 登录窗口关闭时，关闭主窗口并释放资源
            loginWindow.Closed += (s, e) =>
            {
                _mainWindow?.Close();
                if (_mainWindow is IDisposable mainDisposable)
                {
                    mainDisposable.Dispose();
                }
            };

            loginWindow.ShowDialog();
        }

        /// <summary>
        /// 登录提交事件处理（核心逻辑）
        /// </summary>
        private async Task LoginWindow_OnLoginSubmit(object sender, LoginInputEventArgs e)
        {
            try
            {
                // 1. 输入验证（补充基础校验）
                if (string.IsNullOrEmpty(e.Username?.Trim()) || string.IsNullOrEmpty(e.Password?.Trim()))
                {
                    e.LoginSuccess = false;
                    e.Message = "用户名或密码不能为空！";
                    return;
                }

                // 2. MD5加密密码（调用 HttpHelper 静态方法）
                string md5Password = HttpHelper.Md5Encrypt(e.Password.Trim());

                // 3. 从配置文件读取API地址和接口路径
                string apiBaseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? string.Empty;
                string loginRelativePath = ConfigurationManager.AppSettings["LoginRelativePath"] ?? "Login/JWTToken3.0";

                if (string.IsNullOrEmpty(apiBaseUrl))
                {
                    e.LoginSuccess = false;
                    e.Message = "API地址配置错误，请检查App.config！";
                    return;
                }

                // 4. 发送登录请求（调用 HttpHelper，响应类型为 MessageModel<TokenInfoViewModel>）
                Nine.Design.Core.Model.MessageModel<TokenInfoViewModel> loginResult = await HttpHelper.GetAsync<TokenInfoViewModel>(
                    relativePath: loginRelativePath,
                    parameters: new[]
                    {
                        new KeyValuePair<string, string>("name", e.Username.Trim()),
                        new KeyValuePair<string, string>("pass", md5Password)
                    });

                // 5. 处理登录结果（适配原有事件返回逻辑）
                if (loginResult.success && loginResult.response != null && loginResult.response.success)
                {
                    // 登录成功：保存Token信息
                    SaveTokenInfo(loginResult.response);

                    // 打开主窗口
                    _mainWindow = new MainWindow();
                    _mainWindow.Show();

                    // 回传成功结果给登录窗口
                    e.LoginSuccess = true;
                    e.Message = $"登录成功！Token有效期：{Math.Round(loginResult.response.expires_in / 3600, 1)}小时";
                }
                else
                {
                    // 登录失败：回传错误信息
                    e.LoginSuccess = false;
                    e.Message = string.IsNullOrEmpty(loginResult.msg)
                        ? "登录失败，请检查用户名或密码！"
                        : loginResult.msg;
                }
            }
            catch (HttpRequestException ex)
            {
                // 网络异常处理
                e.LoginSuccess = false;
                e.Message = $"网络请求失败：{ex.Message}";
            }
            catch (Exception ex)
            {
                // 其他异常处理
                e.LoginSuccess = false;
                e.Message = $"登录异常：{ex.Message}";
            }
        }

        /// <summary>
        /// 保存Token信息到应用全局属性（供后续接口调用）
        /// </summary>
        private void SaveTokenInfo(TokenInfoViewModel tokenInfo)
        {
            if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.token))
                return;

            // 存储Token、过期时间（秒）、到期时间（本地时间）
            App.Current.Properties["JwtToken"] = tokenInfo.token;
            App.Current.Properties["TokenExpiresIn"] = tokenInfo.expires_in;
            App.Current.Properties["TokenExpireTime"] = DateTime.Now.AddSeconds(tokenInfo.expires_in);
        }

        /// <summary>
        /// 密码加密存储（如果需要“记住密码”功能）
        /// </summary>
        private void SaveEncryptedPassword(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return;

            // 示例：使用MD5加密后存储（实际项目可改用更安全的加密方式）
            string encryptedPwd = HttpHelper.Md5Encrypt(password);

            // 方式1：存储到配置文件（持久化）
            // Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            // config.AppSettings.Settings["RememberedUsername"].Value = username;
            // config.AppSettings.Settings["RememberedPassword"].Value = encryptedPwd;
            // config.Save(ConfigurationSaveMode.Modified);
            // ConfigurationManager.RefreshSection("appSettings");

            // 方式2：存储到全局属性（仅当前运行有效）
            // App.Current.Properties["RememberedUsername"] = username;
            // App.Current.Properties["RememberedPassword"] = encryptedPwd;
        }
    }
}