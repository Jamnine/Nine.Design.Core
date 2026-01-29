using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace Nine.Design.PollingTool
{
    /// <summary>
    /// MachineUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class MachineUserControl : UserControl, INotifyPropertyChanged
    {
        private int _machineId;
        private bool _isPolling;
        private ObservableCollection<string> _results;

        public event PropertyChangedEventHandler PropertyChanged;

        public int MachineId
        {
            get => _machineId;
            set { _machineId = value; OnPropertyChanged(); }
        }

        public bool IsPolling
        {
            get => _isPolling;
            set { _isPolling = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Results
        {
            get => _results;
            set { _results = value; OnPropertyChanged(); }
        }

        public MachineUserControl()
        {
            InitializeComponent();
            DataContext = this;
            Results = new ObservableCollection<string>();
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
