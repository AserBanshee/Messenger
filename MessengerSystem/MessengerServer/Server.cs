using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MessengerProtocol;

namespace MessengerServer
{
    public class Server
    {
        private TcpListener _listener;
        private readonly ConcurrentDictionary<string, ClientSession> _clients;
        private bool _isRunning;

        public event Action<string> OnLog;
        public event Action<string, Message> OnMessageReceived;

        public Server()
        {
            _clients = new ConcurrentDictionary<string, ClientSession>();
        }

        public async Task StartAsync(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;

                Log($"Сервер запущен на порту {port}");

                while (_isRunning)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при запуске сервера: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient)
        {
            var session = new ClientSession(tcpClient, this);
            try
            {
                await session.HandshakeAsync();
                
                _clients[session.UserId] = session;
                Log($"Пользователь {session.UserId} подключился. Всего: {_clients.Count}");
                
                await BroadcastUserListAsync();
                await BroadcastUserStatusAsync(session.UserId, true);

                await session.ProcessMessagesAsync();
            }
            catch (Exception ex)
            {
                Log($"Ошибка в сессии {session.UserId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(session.UserId, out _);
                Log($"Пользователь {session.UserId} отключился. Всего: {_clients.Count}");
                await BroadcastUserStatusAsync(session.UserId, false);
                await BroadcastUserListAsync();
                session.Dispose();
            }
        }

        public async Task RouteMessageAsync(Message message, ClientSession sender)
        {
            OnMessageReceived?.Invoke(sender.UserId, message);
            
            switch (message.Type)
            {
                case MessageType.BroadcastChat:
                    await BroadcastToAllAsync(message, sender.UserId);
                    break;
                    
                case MessageType.PrivateChat:
                    if (_clients.TryGetValue(message.TargetId, out var target))
                    {
                        await target.SendMessageAsync(message);
                        await sender.SendMessageAsync(message); // Echo для отправителя
                    }
                    else
                    {
                        var errorMsg = new Message { Type = MessageType.Status, Success = false, ErrorMessage = "Пользователь не найден" };
                        await sender.SendMessageAsync(errorMsg);
                    }
                    break;
                    
                case MessageType.FileUploadStart:
                case MessageType.FileChunk:
                case MessageType.FileUploadComplete:
                    await HandleFileTransferAsync(message, sender);
                    break;
                    
                case MessageType.FileDownloadRequest:
                    await HandleFileDownloadAsync(message, sender);
                    break;
                    
                default:
                    await sender.SendMessageAsync(new Message { Type = MessageType.Status, Success = false, ErrorMessage = "Неизвестный тип сообщения" });
                    break;
            }
        }

        private async Task HandleFileTransferAsync(Message message, ClientSession sender)
        {
            switch (message.Type)
            {
                case MessageType.FileUploadStart:
                    Log($"Начало загрузки файла {message.FileName} от {sender.UserId} для {message.TargetId}");
                    if (_clients.TryGetValue(message.TargetId, out var recipient))
                    {
                        await recipient.SendMessageAsync(message);
                    }
                    break;
                    
                case MessageType.FileChunk:
                    if (_clients.TryGetValue(message.TargetId, out var target))
                    {
                        await target.SendMessageAsync(message);
                    }
                    break;
                    
                case MessageType.FileUploadComplete:
                    Log($"Файл {message.FileName} успешно передан");
                    if (_clients.TryGetValue(message.TargetId, out var finalTarget))
                    {
                        await finalTarget.SendMessageAsync(message);
                    }
                    break;
            }
        }

        private async Task HandleFileDownloadAsync(Message message, ClientSession sender)
        {
            Log($"Запрос на скачивание файла от {sender.UserId}");
        }

        private async Task BroadcastToAllAsync(Message message, string excludeUserId = null)
        {
            foreach (var client in _clients.Values)
            {
                if (client.UserId != excludeUserId)
                {
                    await client.SendMessageAsync(message);
                }
            }
        }

        private async Task BroadcastUserStatusAsync(string userId, bool connected)
        {
            var statusMsg = new Message
            {
                Type = connected ? MessageType.UserConnected : MessageType.UserDisconnected,
                SenderId = userId,
                Content = userId
            };
            await BroadcastToAllAsync(statusMsg);
        }

        private async Task BroadcastUserListAsync()
        {
            var userListMsg = new Message
            {
                Type = MessageType.UserList,
                Content = string.Join(",", _clients.Keys)
            };
            await BroadcastToAllAsync(userListMsg);
        }

        private void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            OnLog?.Invoke(message);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
        }
    }
}