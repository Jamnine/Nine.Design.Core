namespace Nine.Design.Login.Models
{
    /// <summary>
    /// 登录结果封装，包含成功状态、提示信息和Token（若成功）
    /// </summary>
    public class LoginResult
    {
        /// <summary>
        /// 登录是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 提示信息（失败时为错误原因，成功时可为空）
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 登录成功时的Token信息（失败时为null）
        /// </summary>
        public TokenInfo TokenInfo { get; set; }
    }
}