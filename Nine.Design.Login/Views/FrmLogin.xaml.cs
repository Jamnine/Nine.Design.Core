using Newtonsoft.Json;
using Nine.Design.Login.Abstractions;
using Nine.Design.Login.Helpers;
using Nine.Design.Login.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Nine.Design.Login.Models.LoginInputEventArgs;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Nine.Design.Login.Views
{
    /// <summary>
    /// 登录窗口（FrmLogin.xaml）的交互逻辑类
    /// 功能包含：登录/注册/找回密码界面切换、左侧动态背景、控件交互动画、登录流程处理
    /// </summary>
    public partial class FrmLogin : Window, IDisposable
    {
        private bool _disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 释放托管资源：取消事件订阅、释放文件流/数据库连接等
                // _service.DataChanged -= OnDataChanged;
                // _service?.Dispose();
            }

            // 释放非托管资源（若有，如 IntPtr 指针）
            // ...

            _disposed = true;
        }

        ~FrmLogin()
        {
            Dispose(false);
        }

        #region 1. 预加载资源（图片素材）
        // 退出按钮-鼠标进入时的图片
        readonly BitmapImage Exit_Enter = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/exit_1.png", UriKind.Relative));
        // 退出按钮-鼠标离开时的图片
        readonly BitmapImage Exit_Leave = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/exit_2.png", UriKind.Relative));
        // 输入框-鼠标进入时的背景图
        readonly BitmapImage TextBox_Enter = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/userbox_2.png", UriKind.Relative));
        // 输入框-鼠标离开时的背景图
        readonly BitmapImage TextBox_Leave = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/userbox_1.png", UriKind.Relative));
        // 登录按钮-鼠标进入时的图片
        readonly BitmapImage LoginButton_Enter = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/login_Button_2.png", UriKind.Relative));
        // 登录按钮-鼠标离开时的图片
        readonly BitmapImage LoginButton_Leave = new BitmapImage(new Uri("/Nine.Design.Login;component/Images/login_Button_1.png", UriKind.Relative));

        /// <summary>
        /// 窗口移动状态控制标记
        /// 1：不允许窗口移动（如鼠标在按钮上时）；0：允许移动
        /// </summary>
        static int Head = 0;
        #endregion

        #region 2. 左侧动态背景相关变量
        /// <summary>
        /// 动态背景的点信息数组（9x9矩阵，用于生成多边形）
        /// </summary>
        private PointInfo[,] _points = new PointInfo[9, 9];
        /// <summary>
        /// 随机数对象（用于动态背景的随机位置、颜色生成）
        /// </summary>
        private Random _random = new Random();
        /// <summary>
        /// 动态背景动画计时器（备用，当前未使用）
        /// </summary>
        private DispatcherTimer _timer;
        /// <summary>
        /// 动态背景刷新计时器（控制多边形移动和颜色变化的帧率）
        /// </summary>
        private DispatcherTimer _timerBackground;
        #endregion

        #region 3. 构造函数与初始化
        // 定义事件：当用户点击登录时，将输入信息传递给项目A
        public event LoginSubmitEventHandler OnLoginSubmit;

        /// <summary>
        /// 登录窗口构造函数（入口）
        /// 仅初始化XAML界面，不包含业务逻辑
        /// </summary>
        public FrmLogin()
        {
            InitializeComponent();
            // 加载时读取本地状态（仅回显，不处理逻辑）
            Loaded += FrmLogin_Loaded;
        }

        // 回显记住的密码和状态（仅界面展示）
        private void FrmLogin_Loaded(object sender, RoutedEventArgs e)
        {
            InitLoginButtonStyle();
            // 从本地读取状态（DLL仅负责读取，不处理存储逻辑）
            //var state = LoadLocalState();
            //if (state != null)
            //{
            //    UserName_Box.Text = state.Username;
            //    UserPass_Box.Password = state.RememberPassword ? state.Password : "";
            //    //UserName_Box.SpellCheck  = state.RememberPassword;
            //    //AutoLogin_CheckBox.IsChecked = state.AutoLogin;
            //}
        }

        // 辅助：读取本地状态（DLL仅负责简单存储，不涉及加密等逻辑）
        //private LoginState LoadLocalState()
        //{
        //    // 实际项目中可简化为读取配置文件（加密逻辑由项目A处理）
        //    return new LoginState(); // 示例：返回空状态
        //}
        private void InitLoginButtonStyle()
        {
            Login_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cdcdcd"));
            Login_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cdcdcd"));
            Login_Icon.RenderTransform = new ScaleTransform(1, 1); // 默认大小
            Login_Icon.Effect = null; // 默认无阴影
        }
        // 辅助：保存本地状态（仅存储用户选择的状态）
        private void SaveLocalState(LoginInputEventArgs input)
        {
            // 实际项目中可简化为写入配置文件
        }


        /// <summary>
        /// 设置窗口在屏幕居中显示
        /// 基于屏幕分辨率和窗口尺寸计算居中位置
        /// </summary>
        public void WindowCenter()
        {
            // 窗口顶部距离 = (屏幕高度 - 窗口高度) / 2
            Top = (SystemParameters.PrimaryScreenHeight - 550) / 2;
            // 窗口左侧距离 = (屏幕宽度 - 窗口宽度) / 2
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        }

        /// <summary>
        /// 窗口加载完成事件（Window_Loaded）
        /// 执行初始化：动态背景启动、测试数据填充、窗口居中、登录流程触发
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 初始化动态背景计时器：120帧/秒（1秒刷新120次，确保动画流畅）
            _timerBackground = new System.Windows.Threading.DispatcherTimer();
            _timerBackground.Tick += new EventHandler(PolyAnimation); // 绑定刷新事件
            _timerBackground.Interval = new TimeSpan(0, 0, 0, 0, 1000 / 120);
            _timerBackground.Start();

            // 2. 填充测试数据（用户名：999999，密码：123，方便调试）
            Deo();

            // 3. 初始化动态背景的点和多边形
            Init();

            // 4. 执行窗口进入动画（StartTo为XAML中定义的Storyboard）
            (StartGrid.FindResource("StartTo") as Storyboard).Begin();

            // 5. 窗口居中显示
            WindowCenter();

            //// 6. 触发登录流程（当前为测试用，实际应移除或改为手动触发）
            //UserLogin();
        }

        /// <summary>
        /// 填充测试数据到输入框
        /// 用于开发调试，避免每次输入账号密码
        /// </summary>
        private void Deo()
        {
            UserName_Box.Text = "999999"; // 测试用户名
            UserPass_Box.Focus(); // 密码框获取焦点
            UserPass_Box.Password = "123"; // 测试密码
            SetSelection(UserPass_Box, UserPass_Box.Password.Length, 0); // 光标定位到密码末尾
        }

        /// <summary>
        /// 设置PasswordBox的光标位置和选中长度
        /// PasswordBox无公开的Select方法，通过反射调用私有方法实现
        /// </summary>
        /// <param name="passwordBox">目标PasswordBox控件</param>
        /// <param name="start">光标起始位置</param>
        /// <param name="length">选中字符长度（0为不选中）</param>
        private void SetSelection(PasswordBox passwordBox, int start, int length)
        {
            // 反射获取PasswordBox的私有Select方法（参数：start(int), length(int)）
            passwordBox.GetType()
                       .GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)
                       .Invoke(passwordBox, new object[] { start, length });
        }
        #endregion

        #region 4. 左侧动态背景实现（核心动画逻辑）
        /// <summary>
        /// 初始化动态背景：生成9x9点矩阵 + 多边形 + 颜色动画
        /// 核心逻辑：创建点→生成两种多边形→绑定颜色渐变动画
        /// </summary>
        private void Init()
        {
            #region 4.1 生成9x9点矩阵（每个点包含位置、移动速度、移动范围）
            for (int i = 0; i < 9; i++)
            {
                for (int j = 0; j < 9; j++)
                {
                    // 随机生成X/Y轴的初始移动速度（-11~10之间，除以24控制速度幅度）
                    double x = _random.Next(-11, 11);
                    double y = _random.Next(-6, 6);

                    _points[i, j] = new PointInfo()
                    {
                        X = i * 50, // 点的初始X坐标（列索引×50，控制点间距）
                        Y = j * 67, // 点的初始Y坐标（行索引×67，控制点间距）
                        SpeedX = x / 24, // X轴移动速度
                        SpeedY = y / 24, // Y轴移动速度
                        DistanceX = _random.Next(35, 106), // X轴最大移动范围（35~105像素）
                        DistanceY = _random.Next(20, 40), // Y轴最大移动范围（20~39像素）
                        MovedX = 0, // X轴已移动距离（用于判断是否超出范围）
                        MovedY = 0, // Y轴已移动距离
                        PolygonInfoList = new List<PolygonInfo>() // 该点关联的多边形列表
                    };
                }
            }
            #endregion

            #region 4.2 生成初始颜色（蓝绿色系，随机微调）
            byte r = (byte)_random.Next(0, 11); // 红色通道（0~10，低饱和度）
            byte g = (byte)_random.Next(100, 201); // 绿色通道（100~200，中高饱和度）
            int intb = g + _random.Next(50, 101); // 蓝色通道（基于绿色+50~100，确保蓝绿色系）
            if (intb > 255) intb = 255; // 防止超出255（颜色通道最大值）
            byte b = (byte)intb;
            #endregion

            #region 4.3 生成第一种多边形（上一行2个点 + 下一行1个点）
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    Polygon poly = new Polygon(); // 创建多边形控件
                    // 添加3个顶点（构成三角形）
                    poly.Points.Add(new Point(_points[i, j].X, _points[i, j].Y)); // 点(当前行,当前列)
                    poly.Points.Add(new Point(_points[i + 1, j].X, _points[i + 1, j].Y)); // 点(下一行,当前列)
                    poly.Points.Add(new Point(_points[i + 1, j + 1].X, _points[i + 1, j + 1].Y)); // 点(下一行,下一列)
                    // 记录该点与多边形的关联（用于后续更新顶点位置）
                    _points[i, j].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 0 });
                    _points[i + 1, j].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 1 });
                    _points[i + 1, j + 1].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 2 });
                    // 设置多边形填充色
                    poly.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
                    // 绑定颜色渐变动画
                    SetColorAnimation(poly);
                    // 将多边形添加到背景容器（layout为XAML中的Grid控件）
                    layout.Children.Add(poly);
                }
            }
            #endregion

            #region 4.4 生成第二种多边形（上一行1个点 + 下一行2个点）
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    Polygon poly = new Polygon();
                    // 添加3个顶点（构成另一种三角形，填补第一种的间隙）
                    poly.Points.Add(new Point(_points[i, j].X, _points[i, j].Y)); // 点(当前行,当前列)
                    poly.Points.Add(new Point(_points[i, j + 1].X, _points[i, j + 1].Y)); // 点(当前行,下一列)
                    poly.Points.Add(new Point(_points[i + 1, j + 1].X, _points[i + 1, j + 1].Y)); // 点(下一行,下一列)
                    // 记录点与多边形的关联
                    _points[i, j].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 0 });
                    _points[i, j + 1].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 1 });
                    _points[i + 1, j + 1].PolygonInfoList.Add(new PolygonInfo() { PolygonRef = poly, PointIndex = 2 });
                    // 设置填充色
                    poly.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
                    // 绑定颜色动画
                    SetColorAnimation(poly);
                    // 添加到背景容器
                    layout.Children.Add(poly);
                }
            }
            #endregion
        }

        /// <summary>
        /// 为多边形绑定颜色渐变动画
        /// 动画逻辑：1~4秒随机时长，渐变到新颜色，循环执行
        /// </summary>
        /// <param name="polygon">需要添加动画的多边形控件</param>
        private void SetColorAnimation(UIElement polygon)
        {
            // 1. 随机动画时长（1~4秒，避免所有多边形同时变色）
            Duration dur = new Duration(new TimeSpan(0, 0, _random.Next(1, 5)));

            // 2. 创建故事板（管理动画）
            Storyboard sb = new Storyboard() { Duration = dur };

            // 3. 动画完成后自动重启（实现循环渐变）
            sb.Completed += (S, E) => SetColorAnimation(polygon);

            // 4. 生成目标颜色（同初始色系，随机微调）
            byte r = (byte)_random.Next(0, 11);
            byte g = (byte)_random.Next(100, 201);
            int intb = g + _random.Next(50, 101);
            if (intb > 255) intb = 255;
            byte b = (byte)intb;

            // 5. 创建颜色动画（从当前颜色渐变到目标颜色）
            ColorAnimation ca = new ColorAnimation()
            {
                To = Color.FromRgb(r, g, b), // 目标颜色
                Duration = dur // 动画时长
            };

            // 6. 绑定动画到多边形的Fill.Color属性（填充色）
            Storyboard.SetTarget(ca, polygon);
            Storyboard.SetTargetProperty(ca, new PropertyPath("Fill.Color"));

            // 7. 启动动画
            sb.Children.Add(ca);
            sb.Begin();
        }

        /// <summary>
        /// 动态背景刷新事件（_timerBackground.Tick触发）
        /// 核心逻辑：更新点的位置→刷新多边形顶点→实现移动动画
        /// </summary>
        void PolyAnimation(object sender, EventArgs e)
        {
            // 仅更新中间7x7的点（最外层点固定，避免背景边缘错乱）
            for (int i = 1; i < 8; i++)
            {
                for (int j = 1; j < 8; j++)
                {
                    PointInfo pointInfo = _points[i, j];

                    #region 1. 更新点的位置（按速度移动）
                    pointInfo.X += pointInfo.SpeedX; // X轴位置 += X轴速度
                    pointInfo.Y += pointInfo.SpeedY; // Y轴位置 += Y轴速度
                    pointInfo.MovedX += pointInfo.SpeedX; // 累计X轴移动距离
                    pointInfo.MovedY += pointInfo.SpeedY; // 累计Y轴移动距离
                    #endregion

                    #region 2. 边界检测（超出范围时反向移动）
                    // X轴超出最大范围：反向X轴速度，重置累计距离
                    if (pointInfo.MovedX >= pointInfo.DistanceX || pointInfo.MovedX <= -pointInfo.DistanceX)
                    {
                        pointInfo.SpeedX = -pointInfo.SpeedX;
                        pointInfo.MovedX = 0;
                    }
                    // Y轴超出最大范围：反向Y轴速度，重置累计距离
                    if (pointInfo.MovedY >= pointInfo.DistanceY || pointInfo.MovedY <= -pointInfo.DistanceY)
                    {
                        pointInfo.SpeedY = -pointInfo.SpeedY;
                        pointInfo.MovedY = 0;
                    }
                    #endregion

                    #region 3. 更新关联多边形的顶点位置
                    foreach (PolygonInfo pInfo in pointInfo.PolygonInfoList)
                    {
                        // 找到该点在多边形中的顶点索引，更新位置
                        pInfo.PolygonRef.Points[pInfo.PointIndex] = new Point(pointInfo.X, pointInfo.Y);
                    }
                    #endregion
                }
            }
        }
        #endregion

        #region 5. 登录核心逻辑
        /// <summary>
        /// 登录流程处理（点击登录按钮或按Enter触发）
        /// 逻辑：显示加载动画→（注释）执行登录验证→显示结果
        /// </summary>
        private async Task UserLogin()
        {
            ShowLoadingAnimation();

            // 收集用户输入
            var inputArgs = new LoginInputEventArgs
            {
                Username = UserName_Box.Text.Trim(),
                Password = UserPass_Box.Password.Trim(),
                //RememberPassword = RememberPassword_CheckBox.IsChecked ?? false,
                //AutoLogin = AutoLogin_CheckBox.IsChecked ?? false
            };

            // 关键：等待项目A的登录逻辑执行完毕（异步等待）
            if (OnLoginSubmit != null)
            {
                // 触发事件：通知项目A处理登录逻辑（DLL不参与逻辑）
                await OnLoginSubmit(this, inputArgs); // 等待事件处理完成
            }
            // 接收项目A回传的结果并显示
            HandleLoginResult(inputArgs);

            //// 1. 显示加载界面（设置加载区宽度，执行加载动画）
            //Loading_Grid.Width = 290; // 加载区宽度（与登录表单一致，实现全屏覆盖）
            //Back_Grid.Width = 290;    // 背景遮罩宽度（防止加载时点击表单）
            //// 执行加载进入动画（XAML中定义的"Loading_Enter"，如淡入效果）
            //(Right_Grid.FindResource("Loading_Enter") as Storyboard).Begin();
            //// 执行加载动画（如CirclePointRingLoading的旋转效果）
            //(Loading_Grid.FindResource("Loading_Start") as Storyboard).Begin();

            //// 以下为注释的登录验证逻辑（实际项目需根据需求启用并修改）
            //// 2. 记录异常日志（测试用，记录登录时间）
            //// string S = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss fff");
            //// var D = Convert.ToDateTime(S);

            //// 3. 获取输入的账号密码
            //// string UserCode = UserName_Box.Text.Trim(); // 用户名（去空格）
            //// // 通过反射获取PasswordBox的密码（避免直接暴露密码字符串）
            //// string UserPassWord = System.Runtime.InteropServices.Marshal.PtrToStringBSTR(
            ////     System.Runtime.InteropServices.Marshal.SecureStringToBSTR(this.UserPass_Box.SecurePassword)
            //// ).ToString().Trim();

            //// 4. 拼接请求参数（按后端API要求格式）
            //// string tId = "1"; // 终端ID（后端区分登录设备的标识，可自定义）
            //// string postDataStr = string.Format("account={0}&password={1}&tId={2}", 
            ////     UserCode, UserPassWord, tId); // 拼接账号、密码、终端ID参数

            //// 5. 后端登录API地址
            //// string route = @"/api/GlobaDomain/UserLoginGetToken";

            //// 6. 输入验证（空值判断）
            //// if (string.IsNullOrEmpty(UserCode) || string.IsNullOrEmpty(UserPassWord))
            //// {
            ////     // 若账号或密码为空，在加载区显示错误提示
            ////     Loading_Text.Content = "状态：账户或密码不能为空";
            //// }
            //// else
            //// {
            ////     // 7. 模拟网络延迟（测试用，让加载动画显示更明显）
            ////     Helper.Delay(1000);
            ////     // 8. 调用后端API执行登录（HttpRequest为自定义的HTTP请求工具类）
            ////     string strToken = HttpRequest.HttpGet(route, postDataStr);
            ////     // 9. 解析API返回结果（反序列化为MessageModel<TokenInfoModel>）
            ////     MessageModel<TokenInfoModel> messageModel = JsonConvert.DeserializeObject<MessageModel<TokenInfoModel>>(strToken);
            ////     TokenInfoModel tokenInfoModel = messageModel.Response; // 登录成功返回的Token信息

            ////     // 10. 判断登录结果
            ////     if (messageModel.Success)
            ////     {
            ////         // 10.1 登录成功（Token信息不为空）
            ////         if (tokenInfoModel != null)
            ////         {
            ////             // 在加载区显示成功信息（用户名、Token有效期）
            ////             Loading_Text.Content = "登录成功\n\n" +
            ////                 "欢迎：超级管理员\n" +
            ////                 "Token有效时间：" + tokenInfoModel.ExpiresIn + "s";
            ////             //// 登录成功后操作（如关闭登录窗、打开主窗）
            ////             //// Helper.Delay(1000); // 延迟1秒，让用户看到成功提示
            ////             //// this.Hide(); // 隐藏登录窗
            ////             //// FrmMainWindow frmMainWindow = new FrmMainWindow(); // 初始化主窗
            ////             //// frmMainWindow.Show(); // 显示主窗
            ////         }
            ////         else
            ////         {
            ////             // 10.2 登录成功但Token为空（后端异常）
            ////             Loading_Text.Content = "状态：登录失败\n\n信息：Token获取失败";
            ////         }
            ////     }
            ////     else
            ////     {
            ////         // 10.3 登录失败（显示后端返回的错误信息）
            ////         Loading_Text.Content = "状态：登录失败\n\n信息：" + messageModel.Msg;
            ////     }
            //// }

            // 11. 执行加载错误动画（当前默认触发，实际需根据登录结果判断是否执行）
            // 注：若启用上述登录逻辑，需将此句移到对应分支（失败时执行）
            //(Loading_Grid.FindResource("Loading_Error") as Storyboard).Begin();
        }

        // 处理项目A回传的结果（仅负责界面反馈）
        private void HandleLoginResult(LoginInputEventArgs result)
        {

            //if (result.LoginSuccess)
            //{
            //    // 登录成功：保存状态（仅存储，不处理业务）
            //    SaveLocalState(result);
            //    this.Close(); // 关闭登录界面
            //}
            //else
            //{
            //    // 登录失败：显示错误信息
            //    System.Windows.MessageBox.Show(result.Message, "提示");
            //}
            HideLoadingAnimation();
            // 10. 判断登录结果
            if (result.LoginSuccess)
            {
                this.Hide(); // 隐藏登录窗
                //this.Close(); // 关闭登录界面

                // 10.1 登录成功（Token信息不为空）
                //if (tokenInfoModel != null)
                //{
                //    // 在加载区显示成功信息（用户名、Token有效期）
                //    Loading_Text.Content = "登录成功\n\n" +
                //        "欢迎：超级管理员\n" +
                //        "Token有效时间：" + tokenInfoModel.ExpiresIn + "s";
                //    //// 登录成功后操作（如关闭登录窗、打开主窗）
                //    //// Helper.Delay(1000); // 延迟1秒，让用户看到成功提示
                //    //// this.Hide(); // 隐藏登录窗
                //    //// FrmMainWindow frmMainWindow = new FrmMainWindow(); // 初始化主窗
                //    //// frmMainWindow.Show(); // 显示主窗
                //}
                //else
                //{
                //    // 10.2 登录成功但Token为空（后端异常）
                //    Loading_Text.Content = "状态：登录失败\n\n信息：Token获取失败";
                //}
            }
            else
            {
                // 10.3 登录失败（显示后端返回的错误信息）
                Loading_Text.Content = "状态：登录失败\n\n信息：" + result.Message;
            }
            (Loading_Grid.FindResource("Loading_Error") as Storyboard).Begin();
            
        }

        // 辅助：显示加载动画
        private void ShowLoadingAnimation()
        {
            Loading_Grid.Width = 290;
            (Right_Grid.FindResource("Loading_Enter") as Storyboard)?.Begin();
            (Loading_Grid.FindResource("Loading_Start") as Storyboard)?.Begin();
        }

        // 辅助：隐藏加载动画
        private void HideLoadingAnimation()
        {
            (Loading_Grid.FindResource("Loading_Exit") as Storyboard)?.Begin();
        }

        // 辅助：获取PasswordBox密码
        private string GetPassword()
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(
                System.Runtime.InteropServices.Marshal.SecureStringToBSTR(UserPass_Box.SecurePassword)
            ).Trim();
        }

        /// <summary>
        /// 窗口拖拽事件（鼠标按下时触发）
        /// 控制逻辑：避免在按钮/头像上点击时触发拖拽，防止误操作
        /// </summary>
        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 1. 若鼠标在登录按钮上（按钮处于"鼠标进入"状态），不允许拖拽
            if (Login_Button.IsMouseOver) { return; }
            // 2. 若鼠标在退出按钮上（按钮处于"鼠标进入"状态），不允许拖拽
            //if (Exit_Img.Source == Exit_Enter) { return; }
            if (Exit_Button.IsMouseOver) { return; }
            // 3. 若Head=1（头像处于交互状态），不允许拖拽
            if (Head == 1) { return; }

            // 让隐藏的FouceBox获取焦点（解决输入框聚焦时无法拖拽的问题）
            FouceBox.Focus();
            // 执行窗口拖拽（WPF自带方法，仅支持左键拖拽）
            this.DragMove();
        }
        #endregion

        #region 6. 登录窗口基础控件事件（退出/头像/输入框/按钮）
        //----------------------------------------------------------------->>>>以下是登录窗口各种事件<<<<-------------------------------------------------------

        /// <summary>
        /// 退出按钮-鼠标进入事件
        /// 切换按钮图片为"鼠标进入"状态，提升交互反馈
        /// </summary>
        private void Exit_MouseEnter(object sender, MouseEventArgs e)
        {
            //Exit_Button.Source = Exit_Enter;
            // 悬浮时：线条+填充统一改为浅红色（#FF6666），视觉提示明显
            Exit_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            Exit_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            // 19x19 小尺寸轻微放大（5%），不突兀
            Exit_Icon.RenderTransform = new ScaleTransform(1.05, 1.05);
            // 红色系阴影，增强层次感
            Exit_Icon.Effect = new DropShadowEffect
            {
                BlurRadius = 3,
                Color = (Color)ColorConverter.ConvertFromString("#FF9999"),
                Opacity = 0.4,
                Direction = 270
            };
        }

        /// <summary>
        /// 退出按钮-鼠标离开事件
        /// 切换按钮图片为"鼠标离开"状态，恢复默认样式
        /// </summary>
        private void Exit_MouseLeave(object sender, MouseEventArgs e)
        {
            //Exit_Button.Source = Exit_Leave;
            // 恢复初始样式：浅灰色（#bfbfbf）
            Exit_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bfbfbf"));
            Exit_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bfbfbf"));
            Exit_Icon.RenderTransform = new ScaleTransform(1, 1);
            Exit_Icon.Effect = null;
        }

        /// <summary>
        /// 退出按钮-鼠标点击事件
        /// 执行窗口退出动画，关闭登录窗并结束客户端进程
        /// </summary>
        private void Exit_Img_MouseUp(object sender, MouseEventArgs e)
        {
            
        }

        //---------------------->>>以下是六边形头像Img的各种事件
        /// <summary>
        /// 头像-鼠标进入事件
        /// 执行头像显示动画（如放大/高亮），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Head_MouseEnter(object sender, MouseEventArgs e)
        {
            // 执行头像显示动画（XAML中定义的"Head_Show"）
            (Head_Img.FindResource("Head_Show") as Storyboard).Begin();
            Head = 1; // 设置为1，禁止窗口拖拽（避免鼠标在头像上时误拖拽）
        }

        /// <summary>
        /// 头像-鼠标离开事件
        /// 执行头像隐藏动画（如缩小/恢复），设置Head=0允许窗口拖拽
        /// </summary>
        private void Head_MouseLeave(object sender, MouseEventArgs e)
        {
            // 执行头像隐藏动画（XAML中定义的"Head_Hide"）
            (Head_Img.FindResource("Head_Hide") as Storyboard).Begin();
            Head = 0; // 设置为0，允许窗口拖拽
        }

        /// <summary>
        /// 头像-鼠标点击事件
        /// 执行头像旋转动画，让隐藏的FouceBox获取焦点（取消输入框聚焦）
        /// </summary>
        private void Head_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FouceBox.Focus(); // 取消输入框聚焦，避免点击头像后仍能输入
            // 执行头像旋转动画（XAML中定义的"Head_Mobile"）
            (Head_Img.FindResource("Head_Mobile") as Storyboard).Begin();
        }

        //---------------------->>>以下是登录界面，用户名输入框的事件
        /// <summary>
        /// 用户名输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态，提升交互反馈
        /// </summary>
        private void UserName_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            //UserName_Img.Source = TextBox_Enter; // 切换背景图
            //UserName_Img.Opacity = 1; // 设置背景图不透明（增强视觉效果）
            // 替换原图片切换：图标颜色改为 #686969，透明度设为1（更醒目）
            UserName_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserName_Icon.Opacity = 1;

            // 横线同步改为 #686969，透明度设为1，与图标保持一致
            UserName_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserName_Line.Opacity = 1;

            // 保留原阴影逻辑（如需增强悬浮阴影，可添加以下代码）
            UserName_Line.Effect = new DropShadowEffect
            {
                BlurRadius = 2,
                Color = (Color)ColorConverter.ConvertFromString("#DDDDDD"),
                Opacity = 0.9,
                Direction = 90,
                ShadowDepth = 1
            };
        }

        /// <summary>
        /// 用户名输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态，恢复默认样式
        /// </summary>
        private void UserName_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            //UserName_Img.Source = TextBox_Leave; // 切换背景图
            //UserName_Img.Opacity = 0.8; // 设置背景图半透明（区分默认状态）
            // 替换原图片切换：恢复默认图标颜色 #CCCCCC，透明度 0.8
            UserName_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
            UserName_Icon.Opacity = 0.8;

            // 横线同步恢复默认样式，与图标保持一致
            UserName_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
            UserName_Line.Opacity = 0.8;

            // 恢复默认向上阴影（与默认状态一致）
            UserName_Line.Effect = new DropShadowEffect
            {
                BlurRadius = 2,
                Color = (Color)ColorConverter.ConvertFromString("#EEEEEE"),
                Opacity = 0.8,
                Direction = 90,
                ShadowDepth = 1
            };
        }

        /// <summary>
        /// 用户名输入框-获取焦点事件
        /// 清空默认占位符（"用户名/邮箱"），调整字体大小和颜色（增强输入视觉）
        /// </summary>
        private void UserName_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            //if (UserName_Box.Text == "用户名/邮箱")
            //{
            //    UserName_Box.Text = ""; // 清空占位符
            //    UserName_Box.FontSize = 17; // 字体放大（提升输入可读性）
            //    // 字体颜色改为黑色（默认占位符为灰色，区分输入状态）
            //    UserName_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            //}
            if (UserName_Box.Text == "用户名/邮箱")
            {
                UserName_Box.Text = "";
                UserName_Box.FontSize = 17; // 字体放大（提升输入可读性）
                UserName_Box.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")); // 输入文本颜色
            }
        }

        /// <summary>
        /// 用户名输入框-失去焦点事件
        /// 若输入为空，恢复默认占位符、字体大小和颜色
        /// </summary>
        private void UserName_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            //if (UserName_Box.Text == "")
            //{
            //    UserName_Box.Text = "用户名/邮箱"; // 恢复占位符
            //    UserName_Box.FontSize = 14; // 字体恢复默认大小
            //    // 字体颜色改为灰色（区分占位符和实际输入）
            //    UserName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            //}
            if (string.IsNullOrWhiteSpace(UserName_Box.Text))
            {
                UserName_Box.Text = "用户名/邮箱";
                UserName_Box.FontSize = 14; // 字体恢复默认大小
                UserName_Box.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7F7F7F")); // 提示文本颜色
            }
        }

        //---------------------->>>以下是登录界面，密码输入框的事件
        /// <summary>
        /// 密码输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态，提升交互反馈
        /// </summary>
        private void UserPass_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            // 密码图标变色+提高透明度
            UserPass_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserPass_Icon.Opacity = 1;

            // 横线同步变色+提高透明度+阴影增强
            UserPass_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserPass_Line.Opacity = 1;
            UserPass_Line.Effect = new DropShadowEffect
            {
                BlurRadius = 2,
                Color = (Color)ColorConverter.ConvertFromString("#DDDDDD"),
                Opacity = 0.9,
                Direction = 90,
                ShadowDepth = 1
            };
        }

        /// <summary>
        /// 密码输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态，恢复默认样式
        /// </summary>
        private void UserPass_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            // 恢复密码图标默认样式
            UserPass_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
            UserPass_Icon.Opacity = 0.8;

            // 恢复横线默认样式+阴影
            UserPass_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
            UserPass_Line.Opacity = 0.8;
            UserPass_Line.Effect = new DropShadowEffect
            {
                BlurRadius = 2,
                Color = (Color)ColorConverter.ConvertFromString("#EEEEEE"),
                Opacity = 0.8,
                Direction = 90,
                ShadowDepth = 1
            };
        }

        /// <summary>
        /// 密码输入框-获取焦点事件
        /// 清空密码占位符（通过关联的Label控件"UserPass_Box_Text"实现）
        /// </summary>
        private void UserPass_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            UserPass_Box_Text.Content = ""; // 清空占位符（密码框无Text属性，用Label模拟）
            UserPass_Box_Text.Visibility = Visibility.Collapsed; // 隐藏“密码”提示
                                                                 // 焦点时变主题色（优先级高于悬浮）
            UserPass_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserPass_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#686969"));
            UserPass_Icon.Opacity = 1;
            UserPass_Line.Opacity = 1;
        }

        /// <summary>
        /// 密码输入框-失去焦点事件
        /// 若密码为空，恢复密码占位符（通过Label控件实现）
        /// </summary>
        private void UserPass_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (UserPass_Box.Password == "")
            {
                UserPass_Box_Text.Content = "密码"; // 恢复占位符
            }
            if (string.IsNullOrWhiteSpace(UserPass_Box.Password))
            {
                UserPass_Box_Text.Visibility = Visibility.Visible; // 恢复“密码”提示
                                                                   // 无输入时恢复默认样式
                UserPass_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"));
                UserPass_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"));
                UserPass_Icon.Opacity = 0.8;
                UserPass_Line.Opacity = 0.8;
            }
            else
            {
                // 有输入时保持主题色
                UserPass_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
                UserPass_Line.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#a2a4a7"));
                UserPass_Icon.Opacity = 1;
                UserPass_Line.Opacity = 1;
            }
        }

        /// <summary>
        /// 密码输入框-按键抬起事件
        /// 监听Enter键，按下时触发登录流程（提升操作便捷性）
        /// </summary>
        private async void UserPass_Box_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) // 若按下的是Enter键
            {
                FouceBox.Focus(); // 取消密码框聚焦（避免重复触发）
                await UserLogin(); // 触发登录流程
            }
        }

        //---------------------->>>以下是登录按钮事件
        /// <summary>
        /// 登录按钮（外层Grid）鼠标进入事件
        /// 功能：实现鼠标悬浮时的交互反馈，提升用户体验
        /// </summary>
        /// <param name="sender">事件触发源（此处为Login_Button Grid控件）</param>
        /// <param name="e">鼠标事件参数（包含鼠标位置、状态等信息）</param>
        private void Login_Button_MouseEnter(object sender, MouseEventArgs e)
        {
            // 注释：以下两行是废弃的旧逻辑（原Image控件切换图片/Path直接赋值），已注释保留历史痕迹
            //Login_Button.Source = LoginButton_Enter; // 原Image控件的图片切换逻辑（已替换为Path矢量图，废弃）
            //Login_Button.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007AFF")); // 原直接操作Grid填充色（逻辑错误，废弃）

            // 1. 设置图标线条颜色：从默认浅灰(#bfbfbf)改为深灰(#555555)，增强悬浮视觉对比
            Login_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));

            // 2. 保持图标填充色不变（仍为默认浅灰#bfbfbf），仅通过线条色变化区分状态，避免过于突兀
            Login_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bfbfbf"));

            // 3. 图标中心缩放：以图标中心为原点（依赖XAML中RenderTransformOrigin="0.5,0.5"），轻微放大3%（1.03倍）
            // 注释：原注释"放大15%"为笔误，实际缩放比例为1.03（放大3%），视觉更柔和
            Login_Icon.RenderTransform = new ScaleTransform(1.03, 1.03);

            // 4. 添加悬浮阴影效果：增强图标层次感，提升交互感知
            Login_Icon.Effect = new DropShadowEffect
            {
                BlurRadius = 7, // 阴影模糊程度（7px），数值越大阴影越扩散
                Color = (Color)ColorConverter.ConvertFromString("#666666"), // 阴影颜色（深灰#666666），与线条色协调
                Opacity = 0.3, // 阴影透明度（30%），避免阴影过重影响整体视觉
                Direction = 270 // 阴影投射方向（270°对应向下），符合常规UI设计习惯
            };
        }

        /// <summary>
        /// 登录按钮-鼠标离开事件
        /// 切换按钮图片为"鼠标离开"状态，恢复默认样式
        /// </summary>
        private void Login_Button_MouseLeave(object sender, MouseEventArgs e)
        {
            InitLoginButtonStyle();
            //Login_Button.Source = LoginButton_Leave;
            //Login_Button.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bfbfbf"));

        }

        /// <summary>
        /// 登录按钮-鼠标点击事件
        /// 取消输入框聚焦，触发登录流程
        /// </summary>
        //private void Login_Button_MouseUp(object sender, MouseButtonEventArgs e)
        //{
        //    FouceBox.Focus(); // 取消输入框聚焦（避免点击按钮后仍能输入）
        //    await UserLogin(); // 触发登录流程
        //}
        private async void Login_Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FouceBox.Focus(); // 取消输入框聚焦
            await UserLogin(); // 调用异步登录方法（注意方法名改为 Async 后缀规范）
            // 这里保留你的登录核心逻辑（原有代码不变）
            // 示例：LoginLogic();

            // 恢复样式
            if (Login_Button.IsMouseOver)
            {
                // 鼠标仍在按钮上，恢复hover效果
                Login_Button_MouseEnter(sender, e);
            }
            else
            {
                // 鼠标已离开，恢复默认样式
                InitLoginButtonStyle();
            }
        }

        /// <summary>
        /// 通用方法：创建带下划线的文本（修复 Pen 错误，使用原生下划线）
        /// </summary>
        /// <param name="text">显示文本</param>
        /// <param name="foreground">文字+下划线颜色</param>
        private TextBlock CreateUnderlineText(string text, Brush foreground)
        {
            var run = new Run(text)
            {
                Foreground = foreground, // 文字颜色
                FontSize = 12
            };

            var textBlock = new TextBlock();
            textBlock.Inlines.Add(new Underline(run)); // 原生下划线，颜色与文字同步
            return textBlock;
        }

        /// <summary>
        /// 通用方法：设置标签的下沉/恢复样式
        /// </summary>
        /// <param name="label">目标标签</param>
        /// <param name="isPressed">是否为按压状态</param>
        /// <param name="text">标签文本</param>
        private void SetLabelPressStyle(Label label, bool isPressed, string text)
        {
            if (isPressed)
            {
                // 按压状态：下沉1px + 加深蓝色 + 下划线
                Brush pressedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#005EB8"));
                label.Content = CreateUnderlineText(text, pressedBrush);

                // 下沉核心：修改Margin实现视觉下沉
                var originalMargin = label.Margin;
                label.Margin = new Thickness(originalMargin.Left, originalMargin.Top + 1, originalMargin.Right, originalMargin.Bottom - 1);
            }
            else
            {
                // 恢复状态：原位置 + 主题蓝 + 下划线
                Brush normalBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0078D7"));
                label.Content = CreateUnderlineText(text, normalBrush);

                // 恢复原Margin，避免位置偏移
                if (label.Name == "Register_Button")
                    label.Margin = new Thickness(0, 0, 35, 25);
                else if (label.Name == "Retrieve_Button")
                    label.Margin = new Thickness(0, 0, 35, 5);
            }
        }
        #endregion

        #region 7. 注册界面控件事件（切换/输入框/按钮）
        //----------------------------------------------------------------->>>>以下是注册窗口各种事件<<<<-------------------------------------------------------

        /// <summary>
        /// 注册按钮（登录界面）-鼠标进入事件
        /// 设置Head=1禁止窗口拖拽，避免点击按钮时误拖拽
        /// </summary>
        private void Register_Button_MouseEnter(object sender, MouseEventArgs e)
        {
            SetLabelPressStyle(Register_Button, false, "注册账号");
            //// 1. 文字颜色改为主题蓝色（与输入框、找回密码按钮保持一致）
            //Register_Button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0078D7"));

            //// 2. 添加下划线，增强可点击提示
            //Register_Button.Content = new TextBlock(new Underline(new Run("注册账号")));
            Head = 1;
        }

        /// <summary>
        /// 注册按钮（登录界面）-鼠标离开事件
        /// 设置Head=0允许窗口拖拽，恢复默认状态
        /// </summary>
        private void Register_Button_MouseLeave(object sender, MouseEventArgs e)
        {
            // 恢复默认样式（无下划线+灰色）
            Register_Button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF727272"));
            Register_Button.Content = "注册账号";
            // 确保恢复原Margin
            Register_Button.Margin = new Thickness(0, 0, 35, 25);
            Head = 0;
        }

        /// <summary>
        /// 注册按钮（登录界面）-鼠标点击事件
        /// 执行"登录→注册"界面切换动画，重置登录表单状态
        /// </summary>
        private void Register_Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Register_Button.IsMouseOver)
                SetLabelPressStyle(Register_Button, false, "注册账号");
            else
                Register_Button_MouseLeave(sender, e);

            // 执行界面切换动画（XAML中定义的两个动画，分别控制登录窗隐藏、注册窗显示）
            (Right_Grid.FindResource("Login_To_Register_1") as Storyboard).Begin();
            (Right_Grid.FindResource("Login_To_Register_2") as Storyboard).Begin();

            // 重置登录表单状态（清空输入、恢复占位符）
            UserName_Box.Text = "";
            UserPass_Box.Password = "";
            UserName_Box.Text = "用户名/邮箱"; // 恢复用户名占位符
            UserName_Box.FontSize = 14; // 恢复字体大小
            UserName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127)); // 恢复字体颜色
            UserPass_Box_Text.Content = "密码"; // 恢复密码占位符
        }

        //---------------------->>>以下是注册界面的注册按钮事件
        /// <summary>
        /// 注册按钮（注册界面）-鼠标进入事件
        /// 改变按钮背景色（加深蓝色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Register_MouseEnter(object sender, MouseEventArgs e)
        {
            Register.Background = new SolidColorBrush(Color.FromRgb(83, 145, 255)); // 按钮颜色加深
            Head = 1; // 禁止窗口拖拽
        }

        /// <summary>
        /// 注册按钮（注册界面）-鼠标离开事件
        /// 恢复按钮背景色（默认蓝色），设置Head=0允许窗口拖拽
        /// </summary>
        private void Register_MouseLeave(object sender, MouseEventArgs e)
        {
            Register.Background = new SolidColorBrush(Color.FromRgb(30, 111, 255)); // 恢复默认颜色
            Head = 0; // 允许窗口拖拽
        }

        //---------------------->>>以下是注册界面的返回按钮事件
        /// <summary>
        /// 返回按钮（注册界面）-鼠标进入事件
        /// 改变按钮背景色（浅灰色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Back_Login_MouseEnter(object sender, MouseEventArgs e)
        {
            Back_Login.Background = new SolidColorBrush(Color.FromRgb(232, 232, 232)); // 颜色加深
            Head = 1; // 禁止窗口拖拽
        }

        /// <summary>
        /// 返回按钮（注册界面）-鼠标离开事件
        /// 恢复按钮背景色（白色），设置Head=0允许窗口拖拽
        /// </summary>
        private void Back_Login_MouseLeave(object sender, MouseEventArgs e)
        {
            Back_Login.Background = new SolidColorBrush(Color.FromRgb(247, 247, 247)); // 恢复默认颜色
            Head = 0; // 允许窗口拖拽
        }

        /// <summary>
        /// 返回按钮（注册界面）-鼠标点击事件
        /// 执行"注册→登录"界面切换动画，重置注册表单状态
        /// </summary>
        private void Back_Login_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 执行界面切换动画（登录窗显示、注册窗隐藏）
            (Right_Grid.FindResource("Register_To_Login_1") as Storyboard).Begin();
            (Right_Grid.FindResource("Register_To_Login_2") as Storyboard).Begin();

            // 重置注册表单状态（清空输入、恢复占位符）
            RegisterName_Box.Text = "";
            RegisterPass_Box.Password = "";
            RegisterEmail_Box.Text = "";
            RegisterKey_Box.Text = "";
            RegisterPass_Box_Text.Content = "密码"; // 恢复密码占位符
            RegisterKey_Box.Text = "卡密"; // 恢复卡密占位符
            RegisterKey_Box.FontSize = 14; // 恢复字体大小
            RegisterKey_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127)); // 恢复字体颜色
            RegisterEmail_Box.Text = "邮箱"; // 恢复邮箱占位符
            RegisterEmail_Box.FontSize = 14;
            RegisterEmail_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            RegisterName_Box.Text = "用户名"; // 恢复用户名占位符
            RegisterName_Box.FontSize = 14;
            RegisterName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
        }

        //---------------------->>>以下是注册界面的卡密输入框事件
        /// <summary>
        /// 卡密输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RegisterKey_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RegisterKey_Img.Source = TextBox_Enter;
            RegisterKey_Img.Opacity = 1;
        }

        /// <summary>
        /// 卡密输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RegisterKey_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RegisterKey_Img.Source = TextBox_Leave;
            RegisterKey_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 卡密输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RegisterKey_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterKey_Box.Text == "卡密")
            {
                RegisterKey_Box.Text = "";
                RegisterKey_Box.FontSize = 17;
                RegisterKey_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 卡密输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RegisterKey_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterKey_Box.Text == "")
            {
                RegisterKey_Box.Text = "卡密";
                RegisterKey_Box.FontSize = 14;
                RegisterKey_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }

        //---------------------->>>以下是注册界面的密码输入框事件
        /// <summary>
        /// 注册密码输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RegisterPass_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RegisterPass_Img.Source = TextBox_Enter;
            RegisterPass_Img.Opacity = 1;
        }

        /// <summary>
        /// 注册密码输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RegisterPass_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RegisterPass_Img.Source = TextBox_Leave;
            RegisterPass_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 注册密码输入框-获取焦点事件
        /// 清空密码占位符（通过Label控件实现）
        /// </summary>
        private void RegisterPass_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            RegisterPass_Box_Text.Content = "";
        }

        /// <summary>
        /// 注册密码输入框-失去焦点事件
        /// 若密码为空，恢复占位符
        /// </summary>
        private void RegisterPass_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterPass_Box.Password == "")
            {
                RegisterPass_Box_Text.Content = "密码";
            }
        }

        //---------------------->>>以下是注册界面的邮箱输入框事件
        /// <summary>
        /// 邮箱输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RegisterEmail_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RegisterEmail_Img.Source = TextBox_Enter;
            RegisterEmail_Img.Opacity = 1;
        }

        /// <summary>
        /// 邮箱输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RegisterEmail_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RegisterEmail_Img.Source = TextBox_Leave;
            RegisterEmail_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 邮箱输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RegisterEmail_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterEmail_Box.Text == "邮箱")
            {
                RegisterEmail_Box.Text = "";
                RegisterEmail_Box.FontSize = 17;
                RegisterEmail_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 邮箱输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RegisterEmail_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterEmail_Box.Text == "")
            {
                RegisterEmail_Box.Text = "邮箱";
                RegisterEmail_Box.FontSize = 14;
                RegisterEmail_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }

        //---------------------->>>以下是注册界面的用户名输入框事件
        /// <summary>
        /// 注册用户名输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RegisterName_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RegisterName_Img.Source = TextBox_Enter;
            RegisterName_Img.Opacity = 1;
        }

        /// <summary>
        /// 注册用户名输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RegisterName_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RegisterName_Img.Source = TextBox_Leave;
            RegisterName_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 注册用户名输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RegisterName_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterName_Box.Text == "用户名")
            {
                RegisterName_Box.Text = "";
                RegisterName_Box.FontSize = 17;
                RegisterName_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 注册用户名输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RegisterName_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RegisterName_Box.Text == "")
            {
                RegisterName_Box.Text = "用户名";
                RegisterName_Box.FontSize = 14;
                RegisterName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }
        #endregion

        #region 8. 找回密码界面控件事件（切换/输入框/按钮）
        //----------------------------------------------------------------->>>>以下是找回密码窗口各种事件<<<<-------------------------------------------------------
        private void Retrieve_Button_MouseEnter(object sender, MouseEventArgs e)
        {
            SetLabelPressStyle(Retrieve_Button, false, "找回密码");
            Head = 1;
        }

        private void Retrieve_Button_MouseLeave(object sender, MouseEventArgs e)
        {
            // 恢复默认样式（无下划线+灰色）
            Retrieve_Button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF727272"));
            Retrieve_Button.Content = "找回密码";
            // 确保恢复原Margin
            Retrieve_Button.Margin = new Thickness(0, 0, 35, 5);
            Head = 0;
        }

        private void Retrieve_Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (Retrieve_Button.IsMouseOver)
                SetLabelPressStyle(Retrieve_Button, false, "找回密码");
            else
                Retrieve_Button_MouseLeave(sender, e);

            (Right_Grid.FindResource("Login_To_Retrieve_1") as Storyboard).Begin();
            (Right_Grid.FindResource("Login_To_Retrieve_2") as Storyboard).Begin();

            UserName_Box.Text = ""; UserPass_Box.Password = "";
            UserName_Box.Text = "用户名/邮箱";
            UserName_Box.FontSize = 14;
            UserName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            UserPass_Box_Text.Content = "密码";
        }

        /// <summary>
        /// 修改按钮（找回密码界面）-鼠标进入事件
        /// 改变按钮背景色（加深蓝色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Retrieve_MouseEnter(object sender, MouseEventArgs e)
        {
            Retrieve.Background = new SolidColorBrush(Color.FromRgb(83, 145, 255));
            Head = 1;
        }

        /// <summary>
        /// 修改按钮（找回密码界面）-鼠标离开事件
        /// 恢复按钮背景色（默认蓝色），设置Head=0允许窗口拖拽
        /// </summary>
        private void Retrieve_MouseLeave(object sender, MouseEventArgs e)
        {
            Retrieve.Background = new SolidColorBrush(Color.FromRgb(30, 111, 255));
            Head = 0;
        }

        //---------------------->>>以下是找回密码窗口的返回按钮事件
        /// <summary>
        /// 返回按钮（找回密码界面）-鼠标进入事件
        /// 改变按钮背景色（浅灰色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Retrieve_Back_Login_MouseEnter(object sender, MouseEventArgs e)
        {
            Retrieve_Back_Login.Background = new SolidColorBrush(Color.FromRgb(232, 232, 232));
            Head = 1;
        }

        /// <summary>
        /// 返回按钮（找回密码界面）-鼠标离开事件
        /// 恢复按钮背景色（白色），设置Head=0允许窗口拖拽
        /// </summary>
        private void Retrieve_Back_Login_MouseLeave(object sender, MouseEventArgs e)
        {
            Retrieve_Back_Login.Background = new SolidColorBrush(Color.FromRgb(247, 247, 247));
            Head = 0;
        }

        /// <summary>
        /// 返回按钮（找回密码界面）-鼠标点击事件
        /// 执行"找回密码→登录"界面切换动画，重置找回密码表单状态
        /// </summary>
        private void Retrieve_Back_Login_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 执行界面切换动画（登录窗显示、找回密码窗隐藏）
            (Right_Grid.FindResource("Retrieve_To_Login_1") as Storyboard).Begin();
            (Right_Grid.FindResource("Retrieve_To_Login_2") as Storyboard).Begin();

            // 重置找回密码表单状态（清空输入、恢复占位符）
            RetrieveCode_Box.Text = "";
            RetrieveName_Box.Text = "";
            RetrievePass_Box.Text = "";
            RetrieveCode_Box.Text = "验证码"; // 恢复验证码占位符
            RetrieveCode_Box.FontSize = 14;
            RetrieveCode_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            RetrievePass_Box.Text = "新密码"; // 恢复新密码占位符
            RetrievePass_Box.FontSize = 14;
            RetrievePass_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            RetrieveName_Box.Text = "用户名/邮箱"; // 恢复用户名占位符
            RetrieveName_Box.FontSize = 14;
            RetrieveName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
        }

        //---------------------->>>以下是找回密码窗口的验证码输入框&获取验证码按钮的事件
        /// <summary>
        /// 验证码输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RetrieveCode_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RetrieveCode_Img.Source = TextBox_Enter;
            RetrieveCode_Img.Opacity = 1;
        }

        /// <summary>
        /// 验证码输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RetrieveCode_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RetrieveCode_Img.Source = TextBox_Leave;
            RetrieveCode_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 验证码输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RetrieveCode_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RetrieveCode_Box.Text == "验证码")
            {
                RetrieveCode_Box.Text = "";
                RetrieveCode_Box.FontSize = 17;
                RetrieveCode_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 验证码输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RetrieveCode_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RetrieveCode_Box.Text == "")
            {
                RetrieveCode_Box.Text = "验证码";
                RetrieveCode_Box.FontSize = 14;
                RetrieveCode_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }

        /// <summary>
        /// 获取验证码按钮-鼠标进入事件
        /// 改变按钮背景色（加深蓝色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void CodeButton_MouseEnter(object sender, MouseEventArgs e)
        {
            CodeButton.Background = new SolidColorBrush(Color.FromRgb(83, 145, 255));
            Head = 1;
        }

        /// <summary>
        /// 获取验证码按钮-鼠标离开事件
        /// 恢复按钮背景色（默认蓝色），设置Head=0允许窗口拖拽
        /// </summary>
        private void CodeButton_MouseLeave(object sender, MouseEventArgs e)
        {
            CodeButton.Background = new SolidColorBrush(Color.FromRgb(30, 111, 255));
            Head = 0;
        }

        //---------------------->>>以下是找回密码窗口的新密码输入框事件
        /// <summary>
        /// 新密码输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RetrievePass_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RetrievePass_Img.Source = TextBox_Enter;
            RetrievePass_Img.Opacity = 1;
        }

        /// <summary>
        /// 新密码输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RetrievePass_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RetrievePass_Img.Source = TextBox_Leave;
            RetrievePass_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 新密码输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RetrievePass_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RetrievePass_Box.Text == "新密码")
            {
                RetrievePass_Box.Text = "";
                RetrievePass_Box.FontSize = 17;
                RetrievePass_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 新密码输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RetrievePass_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RetrievePass_Box.Text == "")
            {
                RetrievePass_Box.Text = "新密码";
                RetrievePass_Box.FontSize = 14;
                RetrievePass_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }

        //---------------------->>>以下是找回密码窗口的用户名输入框事件
        /// <summary>
        /// 找回密码用户名输入框-鼠标进入事件
        /// 切换输入框背景图为"鼠标进入"状态
        /// </summary>
        private void RetrieveName_Box_MouseEnter(object sender, MouseEventArgs e)
        {
            RetrieveName_Img.Source = TextBox_Enter;
            RetrieveName_Img.Opacity = 1;
        }

        /// <summary>
        /// 找回密码用户名输入框-鼠标离开事件
        /// 切换输入框背景图为"鼠标离开"状态
        /// </summary>
        private void RetrieveName_Box_MouseLeave(object sender, MouseEventArgs e)
        {
            RetrieveName_Img.Source = TextBox_Leave;
            RetrieveName_Img.Opacity = 0.8;
        }

        /// <summary>
        /// 找回密码用户名输入框-获取焦点事件
        /// 清空占位符，调整字体大小和颜色
        /// </summary>
        private void RetrieveName_Box_GotFocus(object sender, RoutedEventArgs e)
        {
            if (RetrieveName_Box.Text == "用户名/邮箱")
            {
                RetrieveName_Box.Text = "";
                RetrieveName_Box.FontSize = 17;
                RetrieveName_Box.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            }
        }

        /// <summary>
        /// 找回密码用户名输入框-失去焦点事件
        /// 若输入为空，恢复占位符、字体大小和颜色
        /// </summary>
        private void RetrieveName_Box_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RetrieveName_Box.Text == "")
            {
                RetrieveName_Box.Text = "用户名/邮箱";
                RetrieveName_Box.FontSize = 14;
                RetrieveName_Box.Foreground = new SolidColorBrush(Color.FromRgb(127, 127, 127));
            }
        }
        #endregion

        #region 9. 加载界面控件事件
        //----------------------------------------------------------------->>>>以下是加载窗口各种事件<<<<-------------------------------------------------------

        /// <summary>
        /// 加载界面按钮-鼠标进入事件
        /// 改变按钮背景色（加深蓝色），设置Head=1禁止窗口拖拽
        /// </summary>
        private void Loading_Button_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                // 悬浮时背景色提亮（#FF1E6FFF → #FF3A8FFF），更醒目
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A8FFF"));
                // 轻微放大（1.02倍），增强交互感
                border.RenderTransform = new ScaleTransform(1.02, 1.02);
                // 透明度提高到1（原Opacity=0，确保可见）
                border.Opacity = 1;
            }
            Loading_Button.Background = new SolidColorBrush(Color.FromRgb(83, 145, 255));
            Head = 1;
        }

        /// <summary>
        /// 加载界面按钮-鼠标离开事件
        /// 恢复按钮背景色（默认蓝色），设置Head=0允许窗口拖拽
        /// </summary>
        private void Loading_Button_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                // 恢复默认大小（1倍缩放）
                border.RenderTransform = new ScaleTransform(1, 1);
                // 恢复默认背景色（#FF1E6FFF）
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1E6FFF"));
                // 恢复默认透明度（如需隐藏可设为0，根据业务逻辑调整）
                border.Opacity = 0;
            }
            Loading_Button.Background = new SolidColorBrush(Color.FromRgb(30, 111, 255));
            Head = 0;
        }

        /// <summary>
        /// 加载界面按钮-鼠标点击事件
        /// 执行加载界面退出动画，隐藏加载区
        /// </summary>
        private void Loading_Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                // 恢复悬浮样式（放大1.02倍+提亮背景）
                border.RenderTransform = new ScaleTransform(1.02, 1.02);
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A8FFF"));

                // 执行返回核心逻辑（示例）
                // this.Close(); // 关闭当前窗口
                // NavigationService.GoBack(); // 导航返回（如有导航框架）
            }
            // 执行加载区退出动画（如缩小+淡出）
            (Loading_Grid.FindResource("Loading_Exit") as Storyboard).Begin();
            (Right_Grid.FindResource("Loading_Leave") as Storyboard).Begin();
            // 延迟600毫秒（等待动画完成）
            Helper.Delay(600);
            // 隐藏加载区（宽度设为0，完全隐藏）
            Loading_Grid.Width = 0;
            Back_Grid.Width = 0;
        }
        #endregion

        /// <summary>
        /// 登录按钮（外层Grid）鼠标按下事件
        /// 功能：实现鼠标左键按压时的"凹陷"交互反馈，让用户清晰感知点击动作生效
        /// </summary>
        /// <param name="sender">事件触发源（此处为Login_Button Grid控件）</param>
        /// <param name="e">鼠标按钮事件参数（包含点击的按钮类型、状态等信息）</param>
        private void Login_Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 过滤非左键点击：仅响应鼠标左键按压（符合Windows常规交互习惯，避免右键/中键误触发）
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                // 1. 线条颜色加深：从悬浮态深灰(#555555)改为更深灰(#444444)，强化按压视觉层次
                Login_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"));

                // 2. 填充色加深：从默认浅灰(#bfbfbf)改为中灰(#888888)，模拟"按压凹陷"的视觉效果
                Login_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));

                // 3. 中心收缩：以图标中心为原点（依赖XAML中RenderTransformOrigin="0.5,0.5"），收缩5%（0.95倍）
                // 注释：与悬浮态的放大形成对比，直观模拟物理按钮按压后的凹陷感
                Login_Icon.RenderTransform = new ScaleTransform(0.95, 0.95);

                // 4. 阴影弱化：按压时阴影变淡变窄，进一步强化"凹陷"的物理感知（与悬浮态阴影形成反差）
                Login_Icon.Effect = new DropShadowEffect
                {
                    BlurRadius = 1, // 阴影模糊程度（1px），仅保留微弱阴影，模拟按压后的贴靠感
                    Color = Colors.Gray, // 阴影颜色（系统默认灰色），保持视觉统一性
                    Opacity = 0.2 // 阴影透明度（20%），比悬浮态更淡，突出凹陷效果
                };
            }
        }

        private void Exit_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                // 按压时：改为深红色（#FF3333），强化按压反馈
                Exit_Icon.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3333"));
                Exit_Icon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3333"));
                // 轻微收缩（5%），模拟凹陷
                Exit_Icon.RenderTransform = new ScaleTransform(0.95, 0.95);
                // 阴影加深，突出按压感
                Exit_Icon.Effect = new DropShadowEffect
                {
                    BlurRadius = 2,
                    Color = (Color)ColorConverter.ConvertFromString("#FF6666"),
                    Opacity = 0.5
                };
            }
            try
            {
                // 1. 执行退出动画（异步等待，避免 UI 卡死）
                var exitStoryboard = StartGrid.FindResource("ExitTo") as Storyboard;
                exitStoryboard?.Begin();
                Helper.Delay(1100); // 异步等待动画完成（替代同步 Delay）

                // 2. 释放当前窗口资源（若窗口实现了 IDisposable）
                if (this is IDisposable disposableWindow)
                {
                    disposableWindow.Dispose();
                }

                // 3. 关闭所有由该 DLL 打开的窗口（防止残留子窗口）
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var window in Application.Current.Windows.Cast<Window>().ToList())
                    {
                        if (window != this && (window.IsVisible || window.IsLoaded))
                        {
                            window.Close();
                            // 释放子窗口资源
                            if (window is IDisposable childDisposable)
                            {
                                childDisposable.Dispose();
                            }
                        }
                    }
                });

                // 4. 终止当前主进程（无论调用方是谁，都能彻底退出）
                var currentProcess = Process.GetCurrentProcess();
                currentProcess.Kill(); // 强制终止进程（确保无残留）
            }
            catch (Exception ex)
            {
                // 异常时仍强制终止进程，兜底处理
                Process.GetCurrentProcess().Kill();
            }
            //// 执行窗口退出动画（XAML中定义的"ExitTo"，如淡出效果）
            //(StartGrid.FindResource("ExitTo") as Storyboard).Begin();
            //// 延迟1100毫秒（等待动画完成，提升视觉体验）
            //Helper.Delay(1100);
            //// 关闭当前登录窗口
            //this.Close();
            //// 结束客户端进程（防止窗口关闭后进程残留）
            //Process[] myproc = Process.GetProcesses(); // 获取所有正在运行的进程
            //foreach (Process item in myproc)
            //{
            //    // 找到客户端进程（进程名为"Nine.Design.Client"）并结束
            //    if (item.ProcessName == "Nine.Design.Client")
            //    {
            //        item.Kill();
            //    }
            //}
        }

        private void Exit_Img_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 关闭逻辑（示例）
            // this.Close();

            // 恢复样式（鼠标仍在按钮上则保持悬浮红，否则恢复默认）
            if (Exit_Button.IsMouseOver)
            {
                Exit_MouseEnter(sender, e);
            }
            else
            {
                Exit_MouseLeave(sender, e);
            }
        }

        private void Register_Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                SetLabelPressStyle(Register_Button, true, "注册账号");
            }
        }

        private void Retrieve_Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                SetLabelPressStyle(Retrieve_Button, true, "找回密码");
            }
        }

        private void Loading_Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Border border)
            {
                // 下沉收缩：0.95倍缩放，模拟按压凹陷
                border.RenderTransform = new ScaleTransform(0.95, 0.95);
                // 背景色加深（#FF1E6FFF → #FF1256D8），反馈更强烈
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1256D8"));
            }
        }
    }

    #region 辅助类（动态背景相关）
    /// <summary>
    /// 动态背景的点信息类（记录位置、速度、移动范围等）
    /// </summary>
    public class PointInfo
    {
        public double X { get; set; } // X坐标
        public double Y { get; set; } // Y坐标
        public double SpeedX { get; set; } // X轴移动速度
        public double SpeedY { get; set; } // Y轴移动速度
        public double DistanceX { get; set; } // X轴最大移动范围
        public double DistanceY { get; set; } // Y轴最大移动范围
        public double MovedX { get; set; } // X轴已移动距离
        public double MovedY { get; set; } // Y轴已移动距离
        public List<PolygonInfo> PolygonInfoList { get; set; } // 关联的多边形列表
    }

    /// <summary>
    /// 多边形与点的关联信息类（记录点在多边形中的顶点索引）
    /// </summary>
    public class PolygonInfo
    {
        public Polygon PolygonRef { get; set; } // 关联的多边形控件
        public int PointIndex { get; set; } // 点在多边形顶点列表中的索引
    }
    #endregion
}