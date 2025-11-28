// Helpers/HttpHelper.cs
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Nine.Design.Login.Helpers
{
    public static class HttpHelper
    {
        // .NET 4.6+ 和 .NET Core 都支持 HttpClient 的无参构造函数
        // 建议使用 static readonly 来创建一个单例实例，以优化性能和资源使用
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 发送 GET 请求并返回响应内容的字符串形式。
        /// </summary>
        /// <param name="url">请求的 URL。</param>
        /// <returns>一个包含响应内容的 Task。</returns>
        public static async Task<string> GetAsync(string url)
        {
            // 直接使用标准的 async/await 模式，所有支持的框架都能理解
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

            // 如果 HTTP 响应状态码不是成功的（2xx），则抛出异常
            response.EnsureSuccessStatusCode();

            // 异步读取响应内容
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }
}