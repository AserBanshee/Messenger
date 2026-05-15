using System.Windows;

namespace MessengerClient
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new ViewModels.MainViewModel();
            DataContext = viewModel;
            Closed += (s, e) => viewModel.Disconnect();
        }
    }
}