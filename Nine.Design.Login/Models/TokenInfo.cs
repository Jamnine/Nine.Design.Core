using System;

namespace Nine.Design.Login.Models
{
    /// <summary>
    /// 登录成功后返回的Token信息模型
    /// </summary>
    public class TokenInfo
    {
        /// <summary>
        /// 身份验证Token
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Token有效期（秒）
        /// </summary>
        public int ExpiresIn { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Token过期时间（计算属性，便于界面展示）
        /// </summary>
        public DateTime ExpireTime => DateTime.Now.AddSeconds(ExpiresIn);
    }
}