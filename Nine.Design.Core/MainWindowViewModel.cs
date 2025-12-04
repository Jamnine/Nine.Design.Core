using Nine.Design.Clientbase;

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
    }
}
