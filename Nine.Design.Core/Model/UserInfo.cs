namespace Nine.Design.Core.Model
{
    public class UserInfo
    {
        public string UserName { get; set; } = System.Windows.Application.Current.Properties["UserName"].ToString();
        public string RoleName { get; set; } = System.Windows.Application.Current.Properties["RoleName"].ToString();
        public string JwtToken { get; set; } = System.Windows.Application.Current.Properties["JwtToken"].ToString();
        public string TokenExpiresIn { get; set; } = System.Windows.Application.Current.Properties["TokenExpiresIn"].ToString();
        public string TokenExpireTime { get; set; } = System.Windows.Application.Current.Properties["TokenExpireTime"].ToString();
    }
}
