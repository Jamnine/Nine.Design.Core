
namespace Nine.Design.Login
{
    /// <summary>
    /// 项目全局工具类（与你现有工具类对齐，此处为示例）
    /// </summary>
    public static class ClientPluginTools
    {
        public static string AppKey { get; set; } = "default_appkey";
        public static bool Mock { get; set; } = false;

        /// <summary>
        /// 保存Token到本地（如配置文件、缓存）
        /// </summary>
        public static void SaveToken(string token)
        {
            // 实际实现可根据项目需求调整（如写入Config）
            // ConfigurationManager.AppSettings["LoginToken"] = token;
        }
    }
}