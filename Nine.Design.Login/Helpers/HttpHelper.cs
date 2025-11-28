// Helpers/HttpHelper.cs
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Nine.Design.Login.Helpers
{
    public static class HttpHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 跨框架 GET 请求
        /// </summary>
        public static async Task<string> GetAsync(string url)
        {
#if NET45
            // .NET 4.5 不支持 async/await，用 ContinueWith 链式调用
            return await _httpClient.GetAsync(url)
                .ContinueWith(task =>
                {
                    var response = task.Result;
                    response.EnsureSuccessStatusCode();
                    return response.Content.ReadAsStringAsync().Result;
                });
#else
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
#endif
        }
    }
}