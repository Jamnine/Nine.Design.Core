// ViewModels/LoginViewModel.cs
using Nine.Design.Login.Abstractions;
using Nine.Design.Login.Helpers;
using Nine.Design.Login.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Nine.Design.Login.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged, ILoginViewModel
    {
        // 绑定属性（.NET 4.5 需手动实现 INotifyPropertyChanged）
        private string _userName = "用户名/邮箱";
        private string _password;
        private bool _isLoading;

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // 命令（.NET 4.5 自定义 ICommand 实现）
        public ICommand LoginCommand { get; }

        public ICommand SwitchToRegisterCommand => throw new NotImplementedException();

        public ICommand SwitchToRetrieveCommand => throw new NotImplementedException();

        public ICommand CloseCommand => throw new NotImplementedException();

        // 依赖注入登录服务
        private readonly ILoginService _loginService;

        public LoginViewModel(ILoginService loginService)
        {
            _loginService = loginService;
            LoginCommand = new RelayCommand(ExecuteLogin, () => !IsLoading);
        }

        /// <summary>
        /// 执行登录（跨框架异步）
        /// </summary>
        private void ExecuteLogin()
        {
            if (IsLoading) return;

            // 输入验证
            if (string.IsNullOrWhiteSpace(UserName) || UserName == "用户名/邮箱")
            {
                OnLoginMessage("请输入用户名");
                return;
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                OnLoginMessage("请输入密码");
                return;
            }

            IsLoading = true;
            // 用兼容工具类执行异步登录
            CompatibilityHelper.ExecuteAsync(async () =>
            {
                var request = new LoginRequest { UserName = UserName, Password = Password };
                var result = await _loginService.VerifyLoginAsync(request);

                IsLoading = false;
                if (result.Success)
                    OnLoginSuccess(result.TokenInfo);
                else
                    OnLoginMessage(result.Message);
            });
        }

        // 事件定义（通知界面更新）
        public event Action<string> LoginMessage;
        public event Action<TokenInfo> LoginSuccess;
        public event PropertyChangedEventHandler PropertyChanged;

        // 触发事件
        private void OnLoginMessage(string msg) => LoginMessage?.Invoke(msg);
        private void OnLoginSuccess(TokenInfo token) => LoginSuccess?.Invoke(token);
        private void OnPropertyChanged([CallerMemberName] string propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    // .NET 4.5 兼容的 RelayCommand
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}