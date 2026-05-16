using System.Windows;
using System.Windows.Controls;

namespace MessengerClient
{
    public partial class MainWindow : Window
    {
        private readonly ViewModels.MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new ViewModels.MainViewModel();
            DataContext = _viewModel;

            Loaded += async (s, e) => await _viewModel.InitializeAsync();
            Closed += (s, e) => _viewModel.Disconnect();
        }

        public void ScrollMessagesToEnd()
        {
            // Находим ListBox сообщений и прокручиваем вниз
            var messagesListBox = FindName("MessagesListBox") as ListBox;
            if (messagesListBox != null && messagesListBox.Items.Count > 0)
            {
                messagesListBox.ScrollIntoView(messagesListBox.Items[messagesListBox.Items.Count - 1]);
            }
        }
    }
}