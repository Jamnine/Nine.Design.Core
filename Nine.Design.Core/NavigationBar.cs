namespace Nine.Design.Core
{
    public class NavigationBar
    {
        /// <summary>
        /// 菜单ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 父菜单ID
        /// </summary>
        public string Pid { get; set; }

        /// <summary>
        /// 菜单名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 排序号
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// 是否隐藏
        /// </summary>
        public bool IsHide { get; set; }

        /// <summary>
        /// 是否是按钮（非导航菜单）
        /// </summary>
        public bool IsButton { get; set; }

        /// <summary>
        /// 路由路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 图标类名（接口返回的iconCls）
        /// </summary>
        public string IconCls { get; set; }

        /// <summary>
        /// 元数据（包含前端显示标题、图标等）
        /// </summary>
        public NavigationBarMeta Meta { get; set; }

        /// <summary>
        /// 子菜单（树形结构核心）
        /// </summary>
        public List<NavigationBar> Children { get; set; } = new List<NavigationBar>();

        /// <summary>
        /// 用于绑定的图标编码（转换后的Panuon图标）
        /// </summary>
        public string IconCode => GetIconCode();

        #region 私有方法：图标映射（接口图标 → Panuon图标编码）
        // 修正后的方法（核心：把&#xe913;改为\uE913）
        private string GetIconCode()
        {
            // 映射接口的meta.icon/iconCls到Panuon的图标编码（可根据需要扩展）
            var iconKey = string.IsNullOrEmpty(Meta?.Icon) ? IconCls : Meta.Icon;
            return iconKey switch
            {
                "HomeFilled" or "fa-home" => "\uE913", // 首页图标（去掉&#;，e改E，加\u）
                "OfficeBuilding" or "fa-address-book" => "\uE913", // 部门管理图标
                "Lock" or "fa-sitemap" => "\uE913", // 权限管理图标
                "Box" => "\uE913", // 叫号系统图标
                "Checked" or "fa-history" => "\uE913", // 任务调度图标
                "Menu" => "\uE913", // 基础管理图标
                "Notification" => "\uE913", // 护士工作站图标
                "Platform" => "\uE913", // 屏幕管理图标
                _ => "\uE913" // 默认图标
            };
        }
        #endregion
    }

    /// <summary>
    /// 菜单元数据模型（接口中的meta字段）
    /// </summary>
    public class NavigationBarMeta
    {
        public string Title { get; set; }
        public bool RequireAuth { get; set; }
        public bool NoTabPage { get; set; }
        public bool KeepAlive { get; set; }
        public string Icon { get; set; }
    }
}
