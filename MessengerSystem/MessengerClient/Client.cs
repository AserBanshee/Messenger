using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MessengerProtocol;

namespace MessengerClient
{
    public class Client : IDisposable
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        public string UserId { get; private set; }
        public bool IsConnected { get; private set; }
        
        public event Action<Message> OnMessageReceived;
        public event Action<string> OnDisconnected;
        public event Action<string, int> OnFileProgress;

        private readonly ConcurrentDictionary<string, FileStream> _activeUploads;
        private readonly ConcurrentDictionary<string, FileStream> _activeDownloads;

        public Client()
        {
            _activeUploads = new ConcurrentDictionary<string, FileStream>();
            _activeDownloads = new ConcurrentDictionary<string, FileStream>();
        }

        public async Task<bool> ConnectAsync(string serverIp, int port, string userId)
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(serverIp, port);
                _stream = _tcpClient.GetStream();
                UserId = userId;
                
                // Отправка handshake
                var handshake = Message.CreateHandshake(userId);
                await SendMessageAsync(handshake);
                
                // Получение подтверждения
                var response = await ReceiveMessageAsync();
                if (response.Type == MessageType.Status && response.Success)
                {
                    IsConnected = true;
                    _ = Task.Run(ListenForMessagesAsync);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        private async Task ListenForMessagesAsync()
        {
            while (IsConnected && _tcpClient.Connected)
            {
                try
                {
                    var message = await ReceiveMessageAsync();
                    await HandleMessageAsync(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка приема: {ex.Message}");
                    IsConnected = false;
                    OnDisconnected?.Invoke(ex.Message);
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
                    
                case MessageType.FileUploadComplete:
                    CompleteFileTransfer(message);
                    break;
                    
                case MessageType.FileProgress:
                    OnFileProgress?.Invoke(message.FileId, message.ProgressPercent);
                    break;
            }
        }

        public async Task SendBroadcastMessageAsync(string text)
        {
            var message = Message.CreateBroadcast(UserId, text);
            await SendMessageAsync(message);
        }

        public async Task SendPrivateMessageAsync(string targetId, string text)
        {
            var message = Message.CreatePrivate(UserId, targetId, text);
            await SendMessageAsync(message);
        }

        public async Task SendFileAsync(string targetId, string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var startMsg = Message.CreateFileStart(UserId, targetId, fileInfo.Name, fileInfo.Length);
                await SendMessageAsync(startMsg);
                
                const int chunkSize = 64 * 1024; // 64KB
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
                        chunkMsg.FileName = fileInfo.Name;
                        chunkMsg.FileSize = fileInfo.Length;
                        
                        await SendMessageAsync(chunkMsg);
                        offset += bytesRead;
                        
                        int percent = (int)(offset * 100 / fileInfo.Length);
                        OnFileProgress?.Invoke(startMsg.FileId, percent);
                    }
                }
                
                var completeMsg = new Message
                {
                    Type = MessageType.FileUploadComplete,
                    SenderId = UserId,
                    TargetId = targetId,
                    FileId = startMsg.FileId,
                    FileName = fileInfo.Name,
                    Success = true
                };
                await SendMessageAsync(completeMsg);
            }
            catch (Exception ex)
            {
                var errorMsg = new Message { Type = MessageType.Status, Success = false, ErrorMessage = ex.Message };
                await SendMessageAsync(errorMsg);
            }
        }

        private async Task PrepareFileDownloadAsync(Message message)
        {
            string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), message.FileName);
            var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
            _activeDownloads[message.FileId] = fs;
            
            var response = new Message { Type = MessageType.Status, Success = true, Content = "Ready to receive" };
            await SendMessageAsync(response);
        }

        private async Task SaveFileChunkAsync(Message message)
        {
            if (_activeDownloads.TryGetValue(message.FileId, out var fs))
            {
                fs.Seek(message.FileOffset, SeekOrigin.Begin);
                await fs.WriteAsync(message.FileData, 0, message.ChunkSize);
                
                int percent = (int)((message.FileOffset + message.ChunkSize) * 100 / message.FileSize);
                OnFileProgress?.Invoke(message.FileId, percent);
            }
        }

        private void CompleteFileTransfer(Message message)
        {
            if (_activeDownloads.TryRemove(message.FileId, out var fs))
            {
                fs.Close();
                fs.Dispose();
            }
        }

        private async Task SendMessageAsync(Message message)
        {
            if (!IsConnected) return;
            
            byte[] data = MessageSerializer.Serialize(message);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }

        private async Task<Message> ReceiveMessageAsync()
        {
            byte[] lengthBuffer = new byte[4];
            int bytesRead = 0;
            
            while (bytesRead < 4)
            {
                int read = await _stream.ReadAsync(lengthBuffer, bytesRead, 4 - bytesRead);
                if (read == 0) throw new Exception("Connection closed");
                bytesRead += read;
            }
            
            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
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
            _stream?.Close();
            _tcpClient?.Close();
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}