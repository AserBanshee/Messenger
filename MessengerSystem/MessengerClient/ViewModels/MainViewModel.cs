using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MessengerProtocol;

namespace MessengerClient.ViewModels
{
    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsPrivate { get; set; }
        
        public string DisplayText => IsPrivate ? $"[Личное] {Sender}: {Content}" : $"{Sender}: {Content}";
    }

    public class FileTransfer
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public int Progress { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private Client _client;
        private string _userId;
        private string _newMessage;
        private string _selectedUser;
        private string _statusText = "Отключен";
        private Brush _statusColor = Brushes.Red;
        private bool _isConnected;

        public ObservableCollection<ChatMessage> Messages { get; }
        public ObservableCollection<string> Users { get; }
        public ObservableCollection<FileTransfer> ActiveTransfers { get; }

        public string UserId
        {
            get => _userId;
            set { _userId = value; OnPropertyChanged(); }
        }

        public string NewMessage
        {
            get => _newMessage;
            set { _newMessage = value; OnPropertyChanged(); }
        }

        public string SelectedUser
        {
            get => _selectedUser;
            set { _selectedUser = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand SendFileCommand { get; }

        public MainViewModel()
        {
            Messages = new ObservableCollection<ChatMessage>();
            Users = new ObservableCollection<string>();
            ActiveTransfers = new ObservableCollection<FileTransfer>();
            
            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
            DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
            SendMessageCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(NewMessage));
            SendFileCommand = new RelayCommand(async _ => await SendFileAsync(), _ => IsConnected);
            
            // Запрос имени пользователя при запуске
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new Window
                {
                    Title = "Подключение",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(10),
                        Children =
                        {
                            new TextBlock { Text = "Введите имя пользователя:", Margin = new Thickness(0,0,0,5) },
                            new TextBox { Name = "UserNameTextBox", Margin = new Thickness(0,0,0,10) },
                            new Button { Content = "Подключиться", Height = 30, IsDefault = true }
                        }
                    }
                };
                
                var textBox = (TextBox)((StackPanel)dialog.Content).Children[1];
                var button = (Button)((StackPanel)dialog.Content).Children[2];
                
                button.Click += async (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        UserId = textBox.Text;
                        dialog.Close();
                        await ConnectAsync();
                    }
                };
                
                dialog.ShowDialog();
            });
        }

        private async Task ConnectAsync()
        {
            _client = new Client();
            _client.OnMessageReceived += OnMessageReceived;
            _client.OnDisconnected += OnDisconnected;
            _client.OnFileProgress += OnFileProgress;
            
            bool connected = await _client.ConnectAsync("127.0.0.1", 8888, UserId);
            
            if (connected)
            {
                IsConnected = true;
                StatusText = "Подключен";
                StatusColor = Brushes.Green;
                AddSystemMessage("Подключение к серверу установлено");
            }
            else
            {
                StatusText = "Ошибка подключения";
                StatusColor = Brushes.Red;
                AddSystemMessage("Не удалось подключиться к серверу");
            }
        }

        private void Disconnect()
        {
            _client?.DisconnectAsync();
            IsConnected = false;
            StatusText = "Отключен";
            StatusColor = Brushes.Red;
            AddSystemMessage("Отключение от сервера");
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMessage)) return;
            
            if (!string.IsNullOrEmpty(SelectedUser) && SelectedUser != UserId)
            {
                await _client.SendPrivateMessageAsync(SelectedUser, NewMessage);
                Messages.Add(new ChatMessage
                {
                    Sender = UserId,
                    Content = NewMessage,
                    Timestamp = DateTime.Now,
                    IsPrivate = true
                });
            }
            else
            {
                await _client.SendBroadcastMessageAsync(NewMessage);
                Messages.Add(new ChatMessage
                {
                    Sender = UserId,
                    Content = NewMessage,
                    Timestamp = DateTime.Now,
                    IsPrivate = false
                });
            }
            
            NewMessage = string.Empty;
        }

        private async Task SendFileAsync()
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Выберите файл для отправки";
            
            if (dialog.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(SelectedUser) || SelectedUser == UserId)
                {
                    MessageBox.Show("Выберите пользователя из списка для отправки файла", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var transfer = new FileTransfer
                {
                    FileId = Guid.NewGuid().ToString(),
                    FileName = System.IO.Path.GetFileName(dialog.FileName),
                    Progress = 0
                };
                
                Application.Current.Dispatcher.Invoke(() => ActiveTransfers.Add(transfer));
                await _client.SendFileAsync(SelectedUser, dialog.FileName);
            }
        }

        private void OnMessageReceived(Message message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (message.Type)
                {
                    case MessageType.BroadcastChat:
                        Messages.Add(new ChatMessage
                        {
                            Sender = message.SenderId,
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            IsPrivate = false
                        });
                        break;
                        
                    case MessageType.PrivateChat:
                        Messages.Add(new ChatMessage
                        {
                            Sender = message.SenderId,
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            IsPrivate = true
                        });
                        break;
                        
                    case MessageType.UserConnected:
                        if (!Users.Contains(message.SenderId) && message.SenderId != UserId)
                            Users.Add(message.SenderId);
                        AddSystemMessage($"{message.SenderId} подключился");
                        break;
                        
                    case MessageType.UserDisconnected:
                        Users.Remove(message.SenderId);
                        AddSystemMessage($"{message.SenderId} отключился");
                        break;
                        
                    case MessageType.UserList:
                        var users = message.Content.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        Users.Clear();
                        foreach (var user in users.Where(u => u != UserId))
                            Users.Add(user);
                        break;
                }
            });
        }

        private void OnDisconnected(string reason)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                StatusText = $"Отключен: {reason}";
                StatusColor = Brushes.Red;
                AddSystemMessage($"Соединение разорвано: {reason}");
                Users.Clear();
            });
        }

        private void OnFileProgress(string fileId, int percent)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var transfer = ActiveTransfers.FirstOrDefault(t => t.FileId == fileId);
                if (transfer != null)
                {
                    transfer.Progress = percent;
                    if (percent >= 100)
                    {
                        ActiveTransfers.Remove(transfer);
                        AddSystemMessage($"Передача файла {transfer.FileName} завершена");
                    }
                }
            });
        }

        private void AddSystemMessage(string text)
        {
            Messages.Add(new ChatMessage
            {
                Sender = "System",
                Content = text,
                Timestamp = DateTime.Now,
                IsPrivate = false
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Func<object, Task> _asyncExecute;
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Func<object, Task> asyncExecute, Func<object, bool> canExecute = null)
        {
            _asyncExecute = asyncExecute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        
        public async void Execute(object parameter)
        {
            if (_asyncExecute != null)
                await _asyncExecute(parameter);
            else
                _execute?.Invoke(parameter);
        }
        
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}