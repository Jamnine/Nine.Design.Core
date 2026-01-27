using Nine.Design.Core.Http;
using Nine.Design.Core.Model;
using Nine.Design.Core.Model.ViewModels;
using Nine.Design.Login.Models;
using Nine.Design.Login.Views;
using System.Configuration;

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

            // 登录窗口关闭时释放资源
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
        /// 登录提交事件处理（核心逻辑：登录+复用HttpHelper获取用户信息）
        /// </summary>
        private async Task LoginWindow_OnLoginSubmit(object sender, LoginInputEventArgs e)
        {
            try
            {
                // 1. 基础输入验证
                if (string.IsNullOrEmpty(e.Username?.Trim()) || string.IsNullOrEmpty(e.Password?.Trim()))
                {
                    e.LoginSuccess = false;
                    e.Message = "用户名或密码不能为空！";
                    return;
                }

                // 2. MD5加密密码（复用HttpHelper的静态方法）
                string md5Password = HttpHelper.Md5Encrypt(e.Password.Trim());

                // 3. 读取配置文件中的API地址
                string apiBaseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? string.Empty;
                string loginRelativePath = ConfigurationManager.AppSettings["LoginRelativePath"] ?? "Login/JWTToken3.0";

                if (string.IsNullOrEmpty(apiBaseUrl))
                {
                    e.LoginSuccess = false;
                    e.Message = "API地址配置错误，请检查App.config！";
                    return;
                }

                // 4. 发送登录请求（复用HttpHelper.GetAsync）
                var loginResult = await HttpHelper.GetAsync<TokenInfoViewModel>(
                    relativePath: loginRelativePath,
                    parameters: new[]
                    {
                        new KeyValuePair<string, string>("name", e.Username.Trim()),
                        new KeyValuePair<string, string>("pass", md5Password)
                    });

                // 5. 处理登录结果
                if (loginResult.success && loginResult.response != null && loginResult.response.success)
                {
                    // 5.1 保存Token到全局属性
                    SaveTokenToGlobal(loginResult.response);

                    // 5.2 复用HttpHelper调用getInfoByToken接口（核心改造）
                    var userInfoResult = await GetUserInfoByToken(loginResult.response.token);
                    if (userInfoResult.success && userInfoResult.response != null)
                    {
                        // 5.3 保存用户信息到全局属性
                        SaveUserInfoToGlobal(userInfoResult.response);
                        e.Message = $"登录成功！欢迎您：{userInfoResult.response.uRealName}\nToken有效期：{Math.Round(loginResult.response.expires_in / 3600, 1)}小时";
                    }
                    else
                    {
                        e.Message = $"登录成功！Token有效期：{Math.Round(loginResult.response.expires_in / 3600, 1)}小时\n用户信息获取失败：{userInfoResult.msg ?? "接口返回空"}";
                    }

                    // 5.4 打开主窗口
                    _mainWindow = new MainWindow();
                    _mainWindow.Show();

                    // 5.5 回传成功结果
                    e.LoginSuccess = true;
                }
                else
                {
                    // 登录失败
                    e.LoginSuccess = false;
                    e.Message = string.IsNullOrEmpty(loginResult.msg)
                        ? "登录失败，请检查用户名或密码！"
                        : loginResult.msg;
                }
            }
            catch (Exception ex)
            {
                e.LoginSuccess = false;
                e.Message = $"登录异常：{ex.Message}";
            }
        }

        /// <summary>
        /// 复用HttpHelper调用getInfoByToken接口（带Token）
        /// </summary>
        /// <param name="token">登录成功的JWT Token</param>
        /// <returns>MessageModel<UserInfo>（适配HttpHelper的返回格式）</returns>
        private async Task<Model.MessageModel<SysUserInfoDto>> GetUserInfoByToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Model.MessageModel<SysUserInfoDto>.Fail("Token不能为空");
            }

            try
            {
                // 方式1：如果getInfoByToken接口在ApiBaseUrl域名下（推荐）
                // 假设接口相对路径为：api/user/getInfoByToken
                return await HttpHelper.GetWithTokenAsync<SysUserInfoDto>(
                    relativePath: "user/getInfoByToken",
                    token: token,
                    parameters: new[]
                    {
                        new KeyValuePair<string, string>("token", token)
                    });
            }
            catch (Exception ex)
            {
                return Model.MessageModel<SysUserInfoDto>.Fail($"获取用户信息异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 保存Token到全局属性（供UserInfo模型读取）
        /// </summary>
        private void SaveTokenToGlobal(TokenInfoViewModel tokenInfo)
        {
            if (tokenInfo == null) return;

            App.Current.Properties["JwtToken"] = tokenInfo.token;
            App.Current.Properties["TokenExpiresIn"] = tokenInfo.expires_in.ToString();
            App.Current.Properties["TokenExpireTime"] = DateTime.Now.AddSeconds(tokenInfo.expires_in).ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 保存用户信息到全局属性（与UserInfo模型对齐）
        /// </summary>
        private void SaveUserInfoToGlobal(SysUserInfoDto userInfo)
        {
            if (userInfo == null) return;

            App.Current.Properties["UserName"] = userInfo.uRealName;
            App.Current.Properties["RoleName"] = userInfo.name;
            App.Current.Properties["UserId"] = userInfo.uID;
            // Token相关属性已通过SaveTokenToGlobal存储，无需重复赋值
        }

        /// <summary>
        /// 密码加密存储（保留原有逻辑）
        /// </summary>
        private void SaveEncryptedPassword(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return;

            string encryptedPwd = HttpHelper.Md5Encrypt(password);
            // 如需启用“记住密码”，取消以下注释
            // App.Current.Properties["RememberedUsername"] = username;
            // App.Current.Properties["RememberedPassword"] = encryptedPwd;
        }
    }
}