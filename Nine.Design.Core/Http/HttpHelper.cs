using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nine.Design.Core.Model;
using Nine.Design.Core.Model.ViewModels;

namespace Nine.Design.Core.Http
{
    /// <summary>
    /// HTTP请求帮助类（静态类，全局复用，兼容.NET Framework 4.5+）
    /// </summary>
    public static class HttpHelper
    {
        // 从App.config读取API基础地址
        private static readonly string _apiBaseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"] ?? string.Empty;
        // HttpClient单例（避免重复创建连接）
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 静态构造函数（初始化HttpClient配置）
        /// </summary>
        static HttpHelper()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(15); // 15秒超时
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")); // 默认接受JSON响应
        }

        #region 公开API（GET请求）
        /// <summary>
        /// 发送GET请求（无Token，适用于登录等公开接口）
        /// </summary>
        /// <typeparam name="T">响应数据类型（MessageModel的response字段类型）</typeparam>
        /// <param name="relativePath">接口相对路径（如：Login/JWTToken3.0）</param>
        /// <param name="parameters">URL参数（键值对）</param>
        /// <returns>通用返回模型 MessageModel<T></returns>
        public static async Task<MessageModel<T>> GetAsync<T>(string relativePath, params KeyValuePair<string, string>[] parameters)
        {
            return await SendRequestAsync<T>(
                method: HttpMethod.Get,
                relativePath: relativePath,
                parameters: parameters,
                token: null,
                postData: null);
        }

        /// <summary>
        /// 发送GET请求（带Token授权，适用于需要登录的接口）
        /// </summary>
        public static async Task<MessageModel<T>> GetWithTokenAsync<T>(
            string relativePath,
            string token,
            params KeyValuePair<string, string>[] parameters)
        {
            return await SendRequestAsync<T>(
                method: HttpMethod.Get,
                relativePath: relativePath,
                parameters: parameters,
                token: token,
                postData: null);
        }
        #endregion

        #region 公开API（POST请求）
        /// <summary>
        /// 发送POST请求（JSON参数，带Token授权）
        /// </summary>
        public static async Task<MessageModel<T>> PostJsonWithTokenAsync<T>(
            string relativePath,
            object postData,
            string token)
        {
            return await SendRequestAsync<T>(
                method: HttpMethod.Post,
                relativePath: relativePath,
                parameters: null,
                token: token,
                postData: postData);
        }
        #endregion

        #region 核心请求方法（内部封装）
        /// <summary>
        /// 核心请求逻辑（统一处理URL构建、Token、序列化、异常）
        /// </summary>
        private static async Task<MessageModel<T>> SendRequestAsync<T>(
            HttpMethod method,
            string relativePath,
            KeyValuePair<string, string>[] parameters,
            string token = null,
            object postData = null)
        {
            try
            {
                // 1. 构建完整URL（基础地址 + 相对路径 + 参数）
                string fullUrl = BuildFullUrl(relativePath, parameters);
                if (string.IsNullOrEmpty(fullUrl))
                {
                    return MessageModel<T>.Fail("API地址配置错误，请检查App.config中的ApiBaseUrl");
                }

                // 2. 创建HTTP请求消息
                using (HttpRequestMessage request = new HttpRequestMessage(method, fullUrl))
                {
                    // 3. 添加Token授权头（如果有）
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    // 4. 处理POST请求体（JSON格式）
                    if (method == HttpMethod.Post && postData != null)
                    {
                        string jsonContent = JsonConvert.SerializeObject(postData);
                        request.Content = new StringContent(
                            content: jsonContent,
                            encoding: Encoding.UTF8,
                            mediaType: "application/json");
                    }

                    // 5. 发送请求并获取响应
                    using (HttpResponseMessage response = await _httpClient.SendAsync(request))
                    {
                        // 6. 读取响应内容
                        string responseContent = await response.Content.ReadAsStringAsync();

                        // 7. 反序列化为MessageModel<T>（适配已有实体类）
                        MessageModel<T> result = null;
                        try
                        {
                            result = JsonConvert.DeserializeObject<MessageModel<T>>(responseContent);
                        }
                        catch (Exception ex)
                        {
                            return MessageModel<T>.Fail(
                                $"响应解析失败：{ex.Message}，原始响应内容：{responseContent}");
                        }

                        // 8. 处理HTTP错误状态码（如404、500）
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorMsg = result?.msg ??
                                             $"HTTP请求失败，状态码：{response.StatusCode}";
                            return MessageModel<T>.Fail(errorMsg);
                        }

                        // 9. 返回解析结果
                        return result ?? MessageModel<T>.Fail("响应数据为空");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // 网络异常（超时、API不可达等）
                return MessageModel<T>.Fail($"网络请求失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                // 其他未捕获异常
                return MessageModel<T>.Fail($"请求处理异常：{ex.Message}");
            }
        }
        #endregion

        #region 辅助方法（URL构建）
        /// <summary>
        /// 构建完整URL（基础地址 + 相对路径 + URL参数）
        /// </summary>
        /// <param name="relativePath">接口相对路径</param>
        /// <param name="parameters">URL参数（键值对数组）</param>
        /// <returns>完整URL字符串</returns>
        private static string BuildFullUrl(string relativePath, KeyValuePair<string, string>[] parameters)
        {
            // 验证基础地址和相对路径
            if (string.IsNullOrEmpty(_apiBaseUrl))
                return string.Empty;
            if (string.IsNullOrEmpty(relativePath))
                return _apiBaseUrl;

            // 处理基础地址和相对路径的"/"重复问题
            string baseUrl = _apiBaseUrl.TrimEnd('/');
            string path = relativePath.TrimStart('/');
            string url = $"{baseUrl}/{path}";

            // 拼接URL参数（如：?name=xxx&pass=xxx）
            if (parameters != null && parameters.Length > 0)
            {
                StringBuilder paramBuilder = new StringBuilder("?");
                for (int i = 0; i < parameters.Length; i++)
                {
                    KeyValuePair<string, string> param = parameters[i];
                    if (string.IsNullOrEmpty(param.Key))
                        continue;

                    // URL编码参数值（处理中文、空格、@等特殊字符）
                    string encodedValue = WebUtility.UrlEncode(param.Value ?? string.Empty);
                    paramBuilder.Append($"{param.Key}={encodedValue}");

                    // 不是最后一个参数，添加"&"分隔符
                    if (i < parameters.Length - 1)
                        paramBuilder.Append("&");
                }

                url += paramBuilder.ToString();
            }

            return url;
        }
        #endregion

        #region 辅助方法（MD5加密）
        /// <summary>
        /// MD5加密（32位大写，适配登录密码需求）
        /// </summary>
        /// <param name="input">原始字符串（如密码）</param>
        /// <returns>MD5加密后的字符串</returns>
        public static string Md5Encrypt(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            using (System.Security.Cryptography.MD5 md5Hash = System.Security.Cryptography.MD5.Create())
            {
                // 字符串转字节数组（UTF-8编码，支持中文）
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

                // 字节数组转32位大写字符串
                StringBuilder sb = new StringBuilder();
                foreach (byte b in data)
                {
                    sb.Append(b.ToString("X2")); // X2 = 两位十六进制，大写格式
                }

                return sb.ToString();
            }
        }
        #endregion

        #region 辅助方法（Token验证）
        /// <summary>
        /// 检查Token是否过期（需传入Token过期时间）
        /// </summary>
        /// <param name="tokenExpireTime">Token过期时间（UTC时间）</param>
        /// <returns>是否过期</returns>
        public static bool IsTokenExpired(DateTime tokenExpireTime)
        {
            // 本地时间转UTC时间对比（避免时区问题）
            return DateTime.UtcNow >= tokenExpireTime.ToUniversalTime();
        }
        #endregion
    }
}