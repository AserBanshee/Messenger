using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MessengerProtocol;

namespace MessengerClient
{
    public class Client : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        public string UserId { get; private set; } = "";
        public bool IsConnected { get; private set; }

        public event Action<Message>? OnMessageReceived;
        public event Action<string>? OnDisconnected;
        public event Action<string, int>? OnFileProgress;

        public async Task<bool> ConnectAsync(string serverIp, int port, string userId)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(serverIp, port);
                _stream = _tcpClient.GetStream();
                UserId = userId;

                // Отправляем handshake
                var handshake = Message.CreateHandshake(userId);
                byte[] data = MessageSerializer.Serialize(handshake);
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();

                // Ждём подтверждение от сервера (таймаут 5 секунд)
                var receiveTask = ReceiveMessageAsync();
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine("Таймаут при ожидании ответа от сервера");
                    return false;
                }

                var response = await receiveTask;

                // Проверяем ответ
                if (response.Type == MessageType.Status && response.Success)
                {
                    IsConnected = true;
                    _ = Task.Run(ListenForMessagesAsync);
                    System.Diagnostics.Debug.WriteLine($"Успешно подключен как {userId}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка подключения: {response.ErrorMessage ?? "Неизвестная ошибка"}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Client.ConnectAsync: ОШИБКА - {ex.Message}");
                return false;
            }
        }

        private async Task ListenForMessagesAsync()
        {
            while (IsConnected && _tcpClient?.Connected == true)
            {
                try
                {
                    var message = await ReceiveMessageAsync();
                    await HandleMessageAsync(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ListenForMessagesAsync ошибка: {ex.Message}");
                    IsConnected = false;
                    OnDisconnected?.Invoke("Connection lost");
                    break;
                }
            }
        }

        private async Task HandleMessageAsync(Message message)
        {
            switch (message.Type)
            {
                case MessageType.BroadcastChat:
                case MessageType.PrivateChat:
                case MessageType.UserConnected:
                case MessageType.UserDisconnected:
                case MessageType.UserList:
                    OnMessageReceived?.Invoke(message);
                    break;
                case MessageType.FileUploadStart:
                    await PrepareFileDownloadAsync(message);
                    break;
                case MessageType.FileChunk:
                    await SaveFileChunkAsync(message);
                    break;
                case MessageType.Status:
                    System.Diagnostics.Debug.WriteLine($"Получен статус: Success={message.Success}, Error={message.ErrorMessage}");
                    OnMessageReceived?.Invoke(message);
                    break;
            }
        }

        public async Task SendBroadcastMessageAsync(string text)
        {
            await SendMessageAsync(Message.CreateBroadcast(UserId, text));
        }

        public async Task SendPrivateMessageAsync(string targetId, string text)
        {
            await SendMessageAsync(Message.CreatePrivate(UserId, targetId, text));
        }

        public async Task SendFileAsync(string targetId, string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var startMsg = Message.CreateFileStart(UserId, targetId, fileInfo.Name, fileInfo.Length);
                await SendMessageAsync(startMsg);

                const int chunkSize = 64 * 1024;
                byte[] buffer = new byte[chunkSize];

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    long offset = 0;
                    int bytesRead;
                    while ((bytesRead = await fs.ReadAsync(buffer, 0, chunkSize)) > 0)
                    {
                        byte[] chunk = new byte[bytesRead];
                        Array.Copy(buffer, chunk, bytesRead);
                        var chunkMsg = Message.CreateFileChunk(startMsg.FileId, chunk, offset, bytesRead);
                        chunkMsg.TargetId = targetId;
                        await SendMessageAsync(chunkMsg);
                        offset += bytesRead;
                        
                        // Обновляем прогресс
                        int percent = (int)((double)offset / fileInfo.Length * 100);
                        OnFileProgress?.Invoke(startMsg.FileId, percent);
                    }
                }
                
                OnFileProgress?.Invoke(startMsg.FileId, 100);
            }
            catch (Exception ex)
            {
                var errorMsg = new Message { Type = MessageType.Status, Success = false, ErrorMessage = ex.Message };
                await SendMessageAsync(errorMsg);
            }
        }

        private async Task PrepareFileDownloadAsync(Message message)
        {
            // Создаем директорию Downloads если её нет
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MessengerFiles");
            Directory.CreateDirectory(downloadsPath);
            
            string filePath = Path.Combine(downloadsPath, message.FileName ?? "unknown_file");
            
            // Сохраняем информацию о файле для последующих чанков
            // В реальном приложении нужно хранить Dictionary<string, FileStream>
            System.Diagnostics.Debug.WriteLine($"Начинаем загрузку файла: {message.FileName} в {filePath}");
        }

        private async Task SaveFileChunkAsync(Message message)
        {
            // Реализация сохранения чанков файла
            System.Diagnostics.Debug.WriteLine($"Получен чанк файла {message.FileName}, размер: {message.ChunkData?.Length ?? 0}");
        }

        private async Task SendMessageAsync(Message message)
        {
            if (_stream == null) return;
            byte[] data = MessageSerializer.Serialize(message);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }

        private async Task<Message> ReceiveMessageAsync()
        {
            if (_stream == null) return new Message();
            byte[] lengthBuffer = new byte[4];
            int bytesRead = 0;
            while (bytesRead < 4)
            {
                int read = await _stream.ReadAsync(lengthBuffer, bytesRead, 4 - bytesRead);
                if (read == 0) throw new Exception("Connection closed");
                bytesRead += read;
            }
            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            
            if (messageLength <= 0 || messageLength > 1024 * 1024) // Максимум 1MB
                throw new Exception($"Invalid message length: {messageLength}");
                
            byte[] messageBuffer = new byte[messageLength];
            bytesRead = 0;
            while (bytesRead < messageLength)
            {
                int read = await _stream.ReadAsync(messageBuffer, bytesRead, messageLength - bytesRead);
                if (read == 0) throw new Exception("Connection closed");
                bytesRead += read;
            }
            return MessageSerializer.Deserialize(messageBuffer);
        }

        public async Task DisconnectAsync()
        {
            IsConnected = false;
            if (_stream != null)
            {
                await _stream.FlushAsync();
                _stream.Close();
            }
            _tcpClient?.Close();
        }

        public void Dispose() => DisconnectAsync().Wait();
    }
}