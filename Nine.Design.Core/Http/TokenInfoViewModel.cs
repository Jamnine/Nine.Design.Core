namespace Nine.Design.Core.Model.ViewModels
{
    public  class TokenInfoViewModel
    {
        public bool success { get; set; }
        public string token { get; set; }
        public double expires_in { get; set; }
        public string token_type { get; set; }
    }
    public class SysUserInfoDtoRoot<Tkey> where Tkey : IEquatable<Tkey>
    {
        public Tkey uID { get; set; }

        public List<Tkey> RIDs { get; set; }

    }
    public class SysUserInfoDto : SysUserInfoDtoRoot<long>
    {
        public string uLoginName { get; set; }
        public string uLoginPWD { get; set; }
        public string uRealName { get; set; }
        public int uStatus { get; set; }
        public long DepartmentId { get; set; }
        public string uRemark { get; set; }
        public System.DateTime uCreateTime { get; set; }
        public System.DateTime uUpdateTime { get; set; }
        public DateTime uLastErrTime { get; set; }
        public int uErrorCount { get; set; }
        public string name { get; set; }
        public int sex { get; set; } = 0;
        public int age { get; set; }
        public DateTime birth { get; set; }
        public string addr { get; set; }
        public bool tdIsDelete { get; set; }
        public List<string> RoleNames { get; set; }
        public List<long> Dids { get; set; }
        public string DepartmentName { get; set; }
    }
}
