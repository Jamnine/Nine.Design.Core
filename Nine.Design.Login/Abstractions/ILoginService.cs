using Newtonsoft.Json;
using Nine.Design.Login.Models;
using System.Threading.Tasks;

namespace Nine.Design.Login.Abstractions
{
    /// <summary>
    /// 登录服务接口（客户端可实现此接口自定义登录逻辑）
    /// </summary>
    public interface ILoginService
    {
        /// <summary>
        /// 登录验证（核心可重写方法）
        /// </summary>
        /// <param name="request">登录请求参数</param>
        /// <returns>登录结果（含Token信息）</returns>
        Task<LoginResult> VerifyLoginAsync(LoginRequest request);

        /// <summary>
        /// 登录成功后的操作（可重写，如跳转到主界面）
        /// </summary>
        Task OnLoginSuccessAsync(TokenInfo tokenInfo);

        /// <summary>
        /// 登录失败后的操作（可重写，如提示错误）
        /// </summary>
        Task OnLoginFailedAsync(string message);
    }
}
