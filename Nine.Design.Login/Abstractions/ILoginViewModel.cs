using System;
using System.ComponentModel;
using System.Windows.Input;
using Nine.Design.Login.Models;

namespace Nine.Design.Login.Abstractions
{
    /// <summary>
    /// 登录视图模型接口（定义登录界面的核心交互契约）
    /// </summary>
    public interface ILoginViewModel : INotifyPropertyChanged
    {
        // 绑定属性
        string UserName { get; set; }
        string Password { get; set; }
        bool IsLoading { get; set; }

        // 绑定命令
        ICommand LoginCommand { get; }
        ICommand SwitchToRegisterCommand { get; }
        ICommand SwitchToRetrieveCommand { get; }
        ICommand CloseCommand { get; }

        // 事件通知（与界面交互）
        event Action<string> LoginMessage;
        event Action<TokenInfo> LoginSuccess;
    }
}