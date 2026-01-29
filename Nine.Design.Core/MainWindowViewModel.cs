using Nine.Design.Clientbase;
using Nine.Design.Core.Helpers;
using Nine.Design.Core.Http;
using Nine.Design.Core.Model;
using Panuon.WPF.UI;
using System.Data;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Nine.Design.Core
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region 变量声明

        public ICommand MsgClickComCommand { get; private set; }

        public ICommand MenuListBoxSelectedCommand { get; private set; }
        public ICommand MenuTreeViewItemSelectedCommand { get; private set; }

        #endregion

        #region 构造函数
        public MainWindowViewModel()
        {
            Nine.Design.Clientbase.ShowPageHelper.assemblyNames.Add("Nine.Design.Core");
            Nine.Design.Clientbase.ShowPageHelper.namespaceNames.Add("Nine.Design.Core");
            Init();
            InitCommand();

            //InitSubscribe();
        }
        private async void Init()
        {
            //LoadingShow = Visibility.Visible.ToString();
            MenuListShow = Visibility.Visible.ToString();
            //MenuTreeShow = Visibility.Visible.ToString();
            //获取导航栏
            await GetGetNavigationBar();
        }

        protected override void InitCommand()
        {
            MenuTreeViewItemSelectedCommand = new ViewModelCommand((object parameter) => { this.MenuTreeViewItemSelectedExecute(parameter); });
            MenuListBoxSelectedCommand = new ViewModelCommand((object parameter) => { this.MenuListBoxSelectedExecute(parameter); });
            MsgClickComCommand = new ViewModelCommand((object parameter) => { this.MsgClickExecute(); });
        }

        private void MenuListBoxSelectedExecute(object parameter)
        {
            // 1. 获取选中的一级菜单对象
            if (parameter is not NavigationBar selectedMenu)
            {
                ToastHelper.ShowToast("未选中有效菜单");
                return;
            }

            // 2. 🔥 核心修复：安全转换ID，非数字则直接返回
            if (!int.TryParse(selectedMenu.Id?.ToString(), out int menuId))
            {
                // 可选：非数字时的提示（也可以注释掉，完全静默不处理）
                // ToastHelper.ShowToast("菜单ID格式无效，跳过操作");
                return; // 不是数字则不执行后续任何操作
            }

            // 3. 设置全局当前选中的一级菜单ID+名称（仅当ID有效时执行）
            GlobalMenuManager.Instance.SetCurrentFirstLevelMenu(
                menuId: menuId,
                menuName: selectedMenu.Name
            );

            // 4. 打开二级页面（你的原有逻辑）
            LayoutDisplayContent = ShowPageHelper.SelectFrameworkElement("Nine.Design.Core.Views.MainMune");
        }

        private void MsgClickExecute()
        {
            //TriggerStartAnimation = true;
            LoadingShow = Visibility.Visible.ToString();
        }
        #endregion

        #region 接口数据
        #region 获取导航栏
        /// <summary>
        /// 获取左侧菜单接口
        /// </summary>
        /// <returns></returns>
        public async Task<Model.MessageModel<List<NavigationBar>>> GetGetNavigationBar()
        {
            TriggerShowAnimation = true;
            //await Task.Delay(1000);
            LoadingShow = Visibility.Visible.ToString();
            Model.MessageModel<List<NavigationBar>> result = new Model.MessageModel<List<NavigationBar>>();
            try
            {
                string reqUrl = "permission/GetNavigationBar";
                string uid = App.Current.Properties["UserId"]?.ToString() ?? string.Empty;
                string token = App.Current.Properties["JwtToken"]?.ToString() ?? string.Empty;

                // 调用接口获取原始菜单数据
                var apiResult = await HttpHelper.GetWithTokenAsync<NavigationBar>(
                       relativePath: reqUrl,
                       token: token,
                       parameters: new[] { new KeyValuePair<string, string>("uid", uid) });

                if (apiResult?.success == true && apiResult.response != null)
                {
                    // 1. 递归过滤：移除所有按钮项、隐藏项
                    FilterAllButtonItems(apiResult.response);

                    // 2. 提取过滤后的一级菜单（仅保留纯菜单节点）
                    List<NavigationBar> pureMenuList = apiResult.response.Children
                        .Where(m => !m.IsButton && !m.IsHide)
                        .OrderBy(m => m.Order)
                        .ToList();

                    //将数据存入全局菜单管理器
                    GlobalMenuManager.Instance.UpdateGlobalMenuData(
                        originalMenus: apiResult.response.Children?.ToList() ?? new List<NavigationBar>(), // 原始完整数据
                        filteredFirstLevelMenus: pureMenuList // 过滤后的一级菜单
                    );

                    // 3. 赋值给绑定数据源（ListBox/TreeView用）
                    NavigationBarList = pureMenuList;
                    result = Model.MessageModel<List<NavigationBar>>.Success("获取菜单成功", pureMenuList);
                    TriggerHideAnimation = true;
                }
                else
                {
                    result = Model.MessageModel<List<NavigationBar>>.Fail(apiResult?.msg ?? "获取菜单失败");
                }
            }
            catch (Exception ex)
            {
                result = Model.MessageModel<List<NavigationBar>>.Fail($"获取菜单异常：{ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 递归过滤所有层级的按钮项（核心方法）
        /// </summary>
        /// <param name="menuNode">当前菜单节点</param>
        private void FilterAllButtonItems(NavigationBar menuNode)
        {
            // 空值保护：节点为空/无子节点，直接返回
            if (menuNode == null || menuNode.Children == null || menuNode.Children.Count == 0)
                return;

            // 只保留「非按钮 + 非隐藏」的节点
            var validMenuNodes = menuNode.Children
                .Where(m => m != null && !m.IsButton && !m.IsHide)
                .ToList();

            // 清空原有节点，避免残留按钮项
            menuNode.Children.Clear();

            // 递归处理子节点（确保多级菜单的按钮也被过滤）
            foreach (var validNode in validMenuNodes)
            {
                FilterAllButtonItems(validNode);
                menuNode.Children.Add(validNode);
            }
        }
        #endregion
        #endregion

        #region 内部方法
        private void MenuTreeViewItemSelectedExecute(object parameter)
        {

            ToastHelper.ShowToast("<Setter Property=\"Background\" Value=\"#ffb7c5\" /><Setter Property=\"Background\" Value=\"#ffb7c5\" /><Setter Property=\"Background\" Value=\"#ffb7c5\" /><Setter Property=\"Background\" Value=\"#ffb7c5\" /><Setter Property=\"Background\" Value=\"#ffb7c5\" />", MessageBoxIcon.Error);
        }
        #endregion

        #region 属性
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

        private object layoutDisplayContent = null;
        public object LayoutDisplayContent
        {
            get { return this.layoutDisplayContent; }
            set { this.SetProperty(ref this.layoutDisplayContent, value); }
        }

        private object userInfo = new UserInfo();
        public object UserInfo
        {
            get { return this.userInfo; }
            set { this.SetProperty(ref this.userInfo, value); }
        }

        /// <summary>
        /// ListBox 菜单
        /// </summary>
        private string menuListShow = Visibility.Collapsed.ToString();
        public string MenuListShow
        {
            get { return this.menuListShow; }
            set { this.SetProperty(ref this.menuListShow, value); }
        }

        /// <summary>
        /// TreeView 菜单
        /// </summary>
        private string menuTreeShow = Visibility.Collapsed.ToString();
        public string MenuTreeShow
        {
            get { return this.menuTreeShow; }
            set { this.SetProperty(ref this.menuTreeShow, value); }
        }

        /// <summary>
        /// TreeView 菜单
        /// </summary>
        private string loadingShow = Visibility.Collapsed.ToString();
        public string LoadingShow
        {
            get { return this.loadingShow; }
            set { this.SetProperty(ref this.loadingShow, value); }
        }

        #region 动画触发属性
        private bool triggerShowAnimation;
        public bool TriggerShowAnimation
        {
            get { return triggerShowAnimation; }
            set { this.SetProperty(ref triggerShowAnimation, value); }
        }

        private bool triggerHideAnimation;
        public bool TriggerHideAnimation
        {
            get { return triggerHideAnimation; }
            set { this.SetProperty(ref triggerHideAnimation, value); }
        }
        #endregion
        #endregion
    }
}
