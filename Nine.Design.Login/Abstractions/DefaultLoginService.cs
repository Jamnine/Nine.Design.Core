using Newtonsoft.Json;
using Nine.Design.Login.Abstractions;
using Nine.Design.Login.Helpers; // 引用之前的跨框架工具类
using Nine.Design.Login.Models;
using static Nine.Design.Login.Helpers.CompatibilityHelper;

namespace Nine.Design.Login.Services
{
    /// <summary>
    /// ILoginService 的默认实现，封装登录API调用逻辑
    /// </summary>
    public class DefaultLoginService : ILoginService
    {
        /// <summary>
        /// 默认登录验证（客户端必须重写此方法）
        /// </summary>
        public virtual Task<LoginResult> VerifyLoginAsync(LoginRequest request)
        {
            throw new System.NotImplementedException("请实现自定义登录验证逻辑");
        }

        /// <summary>
        /// 默认登录成功操作（关闭登录窗口）
        /// </summary>
        public virtual Task OnLoginSuccessAsync(TokenInfo tokenInfo)
        {
            // 可在此添加默认行为（如关闭登录窗口）
            return TaskHelper.GetCompletedTask();
        }

        /// <summary>
        /// 默认登录失败操作（显示错误信息）
        /// </summary>
        public virtual Task OnLoginFailedAsync(string message)
        {
            // 可在此添加默认提示（如弹窗显示错误）
            return TaskHelper.GetCompletedTask();
        }
    }
}