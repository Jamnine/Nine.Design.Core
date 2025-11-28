using System.Xml.Serialization;

namespace Nine.Design.Login.Models
{
    /// <summary>
    /// 登录请求参数，封装用户名、密码等核心信息
    /// </summary>
    public class LoginRequest:BaseRequest
    {
        public string UserName { get; set; } // 用户名
        public string Password { get; set; } // 密码
    }

    public class BaseRequest
    {
        /// <summary>
        /// 设备型号 P10 F11
        /// </summary>
        public string UseId { get; set; }

        /// <summary>
        /// 设备编码 P10-615
        /// </summary>
        public string UseCode { get; set; }

        /// <summary>
        /// 测试标识 正式:0；测试:1,2....
        /// </summary>
        public int Mock { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// 软件版本
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 链路ID
        /// </summary>
        public long TraceId { get; set; } = 0;
    }

    /// <summary>
    /// 登录输入事件参数（DLL→项目A传递用户输入）
    /// </summary>
    public class LoginInputEventArgs : EventArgs
    {
        public delegate Task LoginSubmitEventHandler(object sender, LoginInputEventArgs e);

        public string Username { get; set; } // 用户名
        public string Password { get; set; } // 密码
        public bool RememberPassword { get; set; } // 记住密码
        public bool AutoLogin { get; set; } // 自动登录

        // 用于项目A回传结果给DLL
        public bool LoginSuccess { get; set; } // 登录是否成功
        public string Message { get; set; } // 结果提示信息
    }
}