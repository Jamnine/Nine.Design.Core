using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nine.Design.Core.Model; // 你的NavigationBar模型命名空间

namespace Nine.Design.Core.Helpers
{
    /// <summary>
    /// 全局菜单数据管理类（单例模式，保证数据唯一且全局可用）
    /// </summary>
    public class GlobalMenuManager
    {
        #region 单例实现（线程安全）
        private static readonly Lazy<GlobalMenuManager> _instance = new Lazy<GlobalMenuManager>(
            () => new GlobalMenuManager(),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 全局唯一实例
        /// </summary>
        public static GlobalMenuManager Instance => _instance.Value;

        // 私有构造函数，禁止外部实例化
        private GlobalMenuManager() { }
        #endregion

        #region 全局菜单数据存储
        // 原始完整菜单数据（未过滤，保留所有节点）
        private List<NavigationBar> _originalAllMenus;
        /// <summary>
        /// 原始完整菜单数据（接口返回的所有菜单，包含一级/二级/按钮等）
        /// </summary>
        public List<NavigationBar> OriginalAllMenus
        {
            get => _originalAllMenus ?? new List<NavigationBar>();
            private set => _originalAllMenus = value;
        }

        // 过滤后的一级菜单（纯菜单节点，用于ListBox/TreeView展示）
        private List<NavigationBar> _filteredFirstLevelMenus;
        /// <summary>
        /// 过滤后的一级菜单（仅纯菜单、未隐藏，已排序）
        /// </summary>
        public List<NavigationBar> FilteredFirstLevelMenus
        {
            get => _filteredFirstLevelMenus ?? new List<NavigationBar>();
            private set => _filteredFirstLevelMenus = value;
        }

        // 🔥 修正为int类型：全局当前选中的一级菜单ID
        private int _currentFirstLevelMenuId;
        /// <summary>
        /// 全局当前选中的一级菜单ID（int类型，匹配NavigationBar.Id）
        /// </summary>
        public int CurrentFirstLevelMenuId
        {
            get => _currentFirstLevelMenuId;
            set
            {
                lock (this) // 加锁保证线程安全
                {
                    _currentFirstLevelMenuId = value;
                }
            }
        }

        // 🔥 新增：全局当前选中的一级菜单名称（可选，方便展示）
        private string _currentFirstLevelMenuName;
        /// <summary>
        /// 全局当前选中的一级菜单名称
        /// </summary>
        public string CurrentFirstLevelMenuName
        {
            get => _currentFirstLevelMenuName ?? string.Empty;
            set
            {
                lock (this)
                {
                    _currentFirstLevelMenuName = value;
                }
            }
        }
        #endregion

        #region 核心方法
        /// <summary>
        /// 初始化/更新全局菜单数据
        /// </summary>
        /// <param name="originalMenus">接口返回的原始菜单数据</param>
        /// <param name="filteredFirstLevelMenus">过滤后的一级菜单</param>
        public void UpdateGlobalMenuData(List<NavigationBar> originalMenus, List<NavigationBar> filteredFirstLevelMenus)
        {
            // 加锁保证线程安全（防止多线程同时更新）
            lock (this)
            {
                OriginalAllMenus = originalMenus ?? new List<NavigationBar>();
                FilteredFirstLevelMenus = filteredFirstLevelMenus ?? new List<NavigationBar>();
            }
        }

        /// <summary>
        /// 🔥 新增：设置当前选中的一级菜单（int类型ID+名称）
        /// </summary>
        /// <param name="menuId">一级菜单ID（int）</param>
        /// <param name="menuName">一级菜单名称（可选）</param>
        public void SetCurrentFirstLevelMenu(int menuId, string menuName = "")
        {
            lock (this)
            {
                CurrentFirstLevelMenuId = menuId;
                CurrentFirstLevelMenuName = menuName;
            }
        }

        /// <summary>
        /// 根据一级菜单ID获取对应的二级菜单列表（int类型）
        /// </summary>
        /// <param name="firstLevelMenuId">一级菜单ID（不传则使用全局当前选中的ID）</param>
        /// <returns>过滤后的二级菜单（纯菜单、未隐藏、已排序）</returns>
        public List<NavigationBar> GetSecondLevelMenusByFirstLevelId(int? firstLevelMenuId = null)
        {
            // 优先使用传入的ID，无则使用全局当前选中的ID
            int targetMenuId = firstLevelMenuId ?? CurrentFirstLevelMenuId;
            if (targetMenuId <= 0) return new List<NavigationBar>();

            // 🔥 修正：int类型ID匹配查询
            var firstLevelMenu = OriginalAllMenus.FirstOrDefault(m => m.Id == targetMenuId.ToString());
            if (firstLevelMenu == null) return new List<NavigationBar>();

            // 过滤二级菜单：仅纯菜单、未隐藏、已排序
            return firstLevelMenu.Children?
                .Where(m => !m.IsButton && !m.IsHide)
                .OrderBy(m => m.Order)
                .ToList() ?? new List<NavigationBar>();
        }

        /// <summary>
        /// 清空全局菜单数据（如退出登录时调用）
        /// </summary>
        public void ClearMenuData()
        {
            lock (this)
            {
                _originalAllMenus = null;
                _filteredFirstLevelMenus = null;
                _currentFirstLevelMenuId = 0; // 清空当前选中ID（重置为0）
                _currentFirstLevelMenuName = null; // 清空当前选中名称
            }
        }

        /// <summary>
        /// 检查菜单数据是否已初始化
        /// </summary>
        public bool IsMenuDataInitialized => OriginalAllMenus.Any() && FilteredFirstLevelMenus.Any();
        #endregion
    }
}