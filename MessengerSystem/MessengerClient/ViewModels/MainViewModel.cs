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
        public string Sender { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsPrivate { get; set; }

        public string DisplayText => IsPrivate
            ? $"[Личное] {Sender}: {Content}"
            : $"{Sender}: {Content}";
    }

    public class FileTransfer
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int Progress { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private Client? _client;
        private string _userId = string.Empty;
        private string _newMessage = string.Empty;
        private string _selectedUser = string.Empty;
        private string _statusText = "Отключен";
        private Brush _statusColor = Brushes.Red;
        private bool _isConnected;
        private bool _isConnecting;
        private int _reconnectAttempts = 0;

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
            set 
            { 
                _newMessage = value; 
                OnPropertyChanged();
                (SendMessageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
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
            set 
            { 
                _isConnected = value; 
                OnPropertyChanged();
                (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SendMessageCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SendFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            set 
            { 
                _isConnecting = value; 
                OnPropertyChanged();
                (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand SendFileCommand { get; }
        public ICommand ReconnectCommand { get; }

        public MainViewModel()
        {
            Messages = new ObservableCollection<ChatMessage>();
            Users = new ObservableCollection<string>();
            ActiveTransfers = new ObservableCollection<FileTransfer>();

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected && !IsConnecting);
            DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
            SendMessageCommand = new RelayCommand(
                async _ => await SendMessageAsync(),
                _ => IsConnected && !string.IsNullOrWhiteSpace(NewMessage));
            SendFileCommand = new RelayCommand(
                async _ => await SendFileAsync(),
                _ => IsConnected);
            ReconnectCommand = new RelayCommand(async _ => await ReconnectAsync(), _ => !IsConnected && !IsConnecting);
        }

        public async Task InitializeAsync()
        {
            string? userName = AskUserName();
            if (string.IsNullOrWhiteSpace(userName))
            {
                Application.Current.Shutdown();
                return;
            }

            UserId = userName;
            await ConnectWithRetryAsync();
        }

        private async Task ConnectWithRetryAsync(int retryCount = 0)
        {
            if (IsConnected || IsConnecting) return;
            
            const int maxRetries = 5;
            
            if (retryCount > 0)
            {
                AddSystemMessage($"Повторная попытка подключения ({retryCount}/{maxRetries})...");
                await Task.Delay(3000); // Ждем 3 секунды перед повторной попыткой
            }
            
            bool connected = await ConnectAsync();
            
            if (!connected && retryCount < maxRetries)
            {
                await ConnectWithRetryAsync(retryCount + 1);
            }
            else if (!connected)
            {
                AddSystemMessage("❌ Не удалось подключиться после нескольких попыток.");
                AddSystemMessage("Убедитесь, что сервер запущен, и нажмите 'Переподключиться'");
                StatusText = "Ошибка подключения";
                StatusColor = Brushes.Red;
            }
        }

        private async Task<bool> ConnectAsync()
        {
            if (IsConnected || IsConnecting) return false;
            
            IsConnecting = true;
            StatusText = "Подключение...";
            StatusColor = Brushes.Orange;
            AddSystemMessage($"Подключение к серверу как '{UserId}'...");

            _client = new Client();
            _client.OnMessageReceived += OnMessageReceived;
            _client.OnDisconnected += OnDisconnected;
            _client.OnFileProgress += OnFileProgress;

            bool connected = false;
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Connect] Попытка подключения к 127.0.0.1:8888 как {UserId}");
                connected = await _client.ConnectAsync("127.0.0.1", 8888, UserId);
                System.Diagnostics.Debug.WriteLine($"[Connect] Результат: {connected}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Connect] Ошибка: {ex.Message}");
                AddSystemMessage($"Ошибка: {ex.Message}");
                IsConnecting = false;
                return false;
            }

            if (connected)
            {
                IsConnected = true;
                _reconnectAttempts = 0;
                StatusText = $"Подключен как {UserId}";
                StatusColor = Brushes.Green;
                AddSystemMessage("✅ Подключение установлено успешно!");
                
                // Запрашиваем список пользователей
                await Task.Delay(500);
                await _client.SendBroadcastMessageAsync("/users");
            }
            else
            {
                StatusText = "Сервер не отвечает";
                StatusColor = Brushes.Red;
                AddSystemMessage("❌ Сервер не отвечает или отказал в подключении");
            }
            
            IsConnecting = false;
            return connected;
        }

        private async Task ReconnectAsync()
        {
            if (IsConnected || IsConnecting) return;
            
            AddSystemMessage("🔄 Попытка переподключения...");
            Messages.Clear();
            Users.Clear();
            ActiveTransfers.Clear();
            
            await ConnectWithRetryAsync();
        }

        public void Disconnect()
        {
            if (_client != null)
            {
                _client.OnMessageReceived -= OnMessageReceived;
                _client.OnDisconnected -= OnDisconnected;
                _client.OnFileProgress -= OnFileProgress;
                
                Task.Run(async () => await _client.DisconnectAsync());
                _client = null;
            }
            
            IsConnected = false;
            StatusText = "Отключен";
            StatusColor = Brushes.Red;
            AddSystemMessage("Отключение от сервера");
            Users.Clear();
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMessage) || _client == null || !IsConnected) return;

            string messageToSend = NewMessage;
            NewMessage = string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(SelectedUser) && SelectedUser != UserId)
                {
                    await _client.SendPrivateMessageAsync(SelectedUser, messageToSend);
                    Messages.Add(new ChatMessage
                    {
                        Sender = UserId,
                        Content = $"→ {SelectedUser}: {messageToSend}",
                        Timestamp = DateTime.Now,
                        IsPrivate = true
                    });
                }
                else
                {
                    await _client.SendBroadcastMessageAsync(messageToSend);
                    Messages.Add(new ChatMessage
                    {
                        Sender = UserId,
                        Content = messageToSend,
                        Timestamp = DateTime.Now,
                        IsPrivate = false
                    });
                }
                
                ScrollToBottom();
            }
            catch (Exception ex)
            {
                AddSystemMessage($"Ошибка отправки: {ex.Message}");
                NewMessage = messageToSend;
            }
        }

        private async Task SendFileAsync()
        {
            if (_client == null || !IsConnected) return;
            
            var dialog = new OpenFileDialog 
            { 
                Title = "Выберите файл",
                Filter = "Все файлы (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(SelectedUser) || SelectedUser == UserId)
                {
                    MessageBox.Show("Выберите пользователя из списка",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var transfer = new FileTransfer
                {
                    FileId = Guid.NewGuid().ToString(),
                    FileName = System.IO.Path.GetFileName(dialog.FileName),
                    Progress = 0
                };

                Application.Current.Dispatcher.Invoke(() => ActiveTransfers.Add(transfer));
                AddSystemMessage($"📤 Отправка '{transfer.FileName}' → {SelectedUser}");

                try
                {
                    await _client.SendFileAsync(SelectedUser, dialog.FileName);
                }
                catch (Exception ex)
                {
                    AddSystemMessage($"❌ Ошибка: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() => ActiveTransfers.Remove(transfer));
                }
            }
        }

        private void OnMessageReceived(Message message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    switch (message.Type)
                    {
                        case MessageType.BroadcastChat:
                            if (!string.IsNullOrEmpty(message.SenderId) && message.SenderId != UserId)
                            {
                                Messages.Add(new ChatMessage
                                {
                                    Sender = message.SenderId,
                                    Content = message.Content ?? string.Empty,
                                    Timestamp = message.Timestamp,
                                    IsPrivate = false
                                });
                                ScrollToBottom();
                            }
                            break;

                        case MessageType.PrivateChat:
                            if (!string.IsNullOrEmpty(message.SenderId))
                            {
                                Messages.Add(new ChatMessage
                                {
                                    Sender = message.SenderId,
                                    Content = $"[личное] {message.Content}",
                                    Timestamp = message.Timestamp,
                                    IsPrivate = true
                                });
                                ScrollToBottom();
                            }
                            break;

                        case MessageType.UserConnected:
                            if (!string.IsNullOrEmpty(message.SenderId) && message.SenderId != UserId && !Users.Contains(message.SenderId))
                            {
                                Users.Add(message.SenderId);
                                AddSystemMessage($"✨ {message.SenderId} присоединился");
                            }
                            break;

                        case MessageType.UserDisconnected:
                            if (!string.IsNullOrEmpty(message.SenderId))
                            {
                                Users.Remove(message.SenderId);
                                AddSystemMessage($"👋 {message.SenderId} покинул чат");
                            }
                            break;

                        case MessageType.UserList:
                            if (!string.IsNullOrEmpty(message.Content))
                            {
                                var parts = message.Content
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(u => u.Trim())
                                    .Where(u => !string.IsNullOrEmpty(u) && u != UserId)
                                    .ToList();
                                
                                Users.Clear();
                                foreach (var u in parts) Users.Add(u);
                                
                                AddSystemMessage($"👥 Пользователей онлайн: {parts.Count}");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OnMessageReceived: {ex.Message}");
                }
            });
        }

        private void OnDisconnected(string reason)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                StatusText = "Отключен";
                StatusColor = Brushes.Red;
                AddSystemMessage($"⚠️ Соединение потеряно: {reason}");
                Users.Clear();
                _client = null;
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
                        AddSystemMessage($"✅ Файл '{transfer.FileName}' отправлен");
                    }
                }
            });
        }

        private void AddSystemMessage(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Messages.Add(new ChatMessage
                {
                    Sender = "System",
                    Content = text,
                    Timestamp = DateTime.Now,
                    IsPrivate = false
                });
                ScrollToBottom();
            });
        }

        private void ScrollToBottom()
        {
            // Этот метод будет вызывать прокрутку в UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.ScrollMessagesToEnd();
            });
        }

        private string? AskUserName()
        {
            string? result = null;

            var win = new Window
            {
                Title = "Подключение",
                Width = 350,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Введите имя пользователя:",
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });

            var textBox = new System.Windows.Controls.TextBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                Height = 30,
                FontSize = 14
            };

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "Подключиться",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Отмена",
                Width = 80,
                Height = 32,
                IsCancel = true
            };

            okButton.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    result = textBox.Text.Trim();
                    win.DialogResult = true;
                    win.Close();
                }
            };

            cancelButton.Click += (s, e) => win.Close();

            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    result = textBox.Text.Trim();
                    win.DialogResult = true;
                    win.Close();
                }
                if (e.Key == Key.Escape) win.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(textBox);
            panel.Children.Add(buttonPanel);
            win.Content = panel;

            win.Loaded += (s, e) => textBox.Focus();
            win.ShowDialog();

            return result;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Func<object?, Task>? _asyncExecute;
        private readonly Action<object?>? _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public RelayCommand(Func<object?, Task> asyncExecute, Func<object?, bool>? canExecute = null)
        {
            _asyncExecute = asyncExecute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public async void Execute(object? parameter)
        {
            if (_asyncExecute != null)
                await _asyncExecute(parameter);
            else
                _execute?.Invoke(parameter);
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}