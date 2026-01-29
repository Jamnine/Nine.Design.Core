using Nine.Design.Clientbase;
using Nine.Design.Core.Helpers;
using Nine.Design.Core.Http;
using Nine.Design.Core.Model;
using Panuon.WPF.UI;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Nine.Design.Core
{
    public class MainMuneViewModel : ViewModelBase
    {
        #region 变量声明
        /// <summary>
        /// 导航栏
        /// </summary>
        private List<NavigationBar> navigationBarList = new List<NavigationBar>();
        /// <summary>
        /// 导航栏
        /// </summary>
        public List<NavigationBar> NavigationBarList
        {
            get { return this.navigationBarList; }
            set { this.SetProperty(ref this.navigationBarList, value); }
        }
        #endregion

        #region 构造函数
        public MainMuneViewModel()
        {
            // 初始化并加载二级菜单
            LoadSecondLevelMenus();
        }

        /// <summary>
        /// 加载当前选中一级菜单对应的二级菜单
        /// </summary>
        private void LoadSecondLevelMenus()
        {
            // 1. 获取全局当前选中的一级菜单ID（int类型）
            int currentFirstLevelId = GlobalMenuManager.Instance.CurrentFirstLevelMenuId;
            if (currentFirstLevelId <= 0)
            {
                NavigationBarList = new List<NavigationBar>(); // 清空列表
                return;
            }

            // 2. 从全局管理器获取二级菜单
            var secondLevelMenus = GlobalMenuManager.Instance.GetSecondLevelMenusByFirstLevelId(currentFirstLevelId);

            // 3. 赋值给List，并触发UI更新
            NavigationBarList = secondLevelMenus.ToList(); // 转为List（确保新实例）
        }
    }

    #endregion

    #region 接口数据

    #endregion

    #region 内部方法

    #endregion

    #region 属性

    #endregion
}
