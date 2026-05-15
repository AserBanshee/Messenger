using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MessengerProtocol;

namespace MessengerServer
{
    public class ClientSession : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly Server _server;
        public string UserId { get; private set; }

        public ClientSession(TcpClient tcpClient, Server server)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _server = server;
        }

        public async Task HandshakeAsync()
        {
            var handshakeMsg = await ReceiveMessageAsync();
            if (handshakeMsg.Type != MessageType.Handshake)
                throw new Exception("Invalid handshake");
            
            UserId = handshakeMsg.SenderId;
            
            var ack = new Message { Type = MessageType.Status, Success = true, Content = "Connected" };
            await SendMessageAsync(ack);
        }

        public async Task ProcessMessagesAsync()
        {
            while (_tcpClient.Connected)
            {
                try
                {
                    var message = await ReceiveMessageAsync();
                    await _server.RouteMessageAsync(message, this);
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка обработки сообщения: {ex.Message}");
                    break;
                }
            }
        }

        private async Task<Message> ReceiveMessageAsync()
        {
            byte[] lengthBuffer = new byte[4];
            int bytesRead = 0;
            
            while (bytesRead < 4)
            {
                int read = await _stream.ReadAsync(lengthBuffer, bytesRead, 4 - bytesRead);
                if (read == 0) throw new IOException("Connection closed");
                bytesRead += read;
            }
            
            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            byte[] messageBuffer = new byte[messageLength];
            bytesRead = 0;
            
            while (bytesRead < messageLength)
            {
                int read = await _stream.ReadAsync(messageBuffer, bytesRead, messageLength - bytesRead);
                if (read == 0) throw new IOException("Connection closed");
                bytesRead += read;
            }
            
            return MessageSerializer.Deserialize(messageBuffer);
        }

        public async Task SendMessageAsync(Message message)
        {
            if (!_tcpClient.Connected) return;
            
            byte[] data = MessageSerializer.Serialize(message);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }

        public void Dispose()
        {
            _stream?.Close();
            _tcpClient?.Close();
        }
    }
}