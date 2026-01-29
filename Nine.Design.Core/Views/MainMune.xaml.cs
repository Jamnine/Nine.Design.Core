using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nine.Design.Core.Views
{
    /// <summary>
    /// MainMune.xaml 的交互逻辑
    /// </summary>
    public partial class MainMune : UserControl
    {
        public MainMune()
        {
            InitializeComponent();
            DataContext = new MainMuneViewModel();
        }
    }
}
