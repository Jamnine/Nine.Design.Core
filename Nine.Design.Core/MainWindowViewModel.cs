using Nine.Design.Clientbase;
using Nine.Design.Core.Model;

namespace Nine.Design.Core
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            //Init();
            //InitSubscribe();
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
    }
}
