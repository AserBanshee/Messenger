using System;

namespace MessengerProtocol
{
    [Serializable]
    public class Message
    {
        public MessageType Type { get; set; }
        public string SenderId { get; set; }
        public string TargetId { get; set; } // Для личных сообщений / целевого пользователя
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        
        // Для файлов
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public long FileOffset { get; set; }
        public byte[] FileData { get; set; }
        public int ChunkSize { get; set; }
        public string FileId { get; set; }
        public int ProgressPercent { get; set; }
        
        // Статус
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public Message()
        {
            Timestamp = DateTime.Now;
            FileId = Guid.NewGuid().ToString();
        }

        // Фабричные методы для удобства
        public static Message CreateHandshake(string userId) => new Message
        {
            Type = MessageType.Handshake,
            SenderId = userId,
            Content = userId
        };

        public static Message CreateBroadcast(string senderId, string text) => new Message
        {
            Type = MessageType.BroadcastChat,
            SenderId = senderId,
            Content = text
        };

        public static Message CreatePrivate(string senderId, string targetId, string text) => new Message
        {
            Type = MessageType.PrivateChat,
            SenderId = senderId,
            TargetId = targetId,
            Content = text
        };

        public static Message CreateFileStart(string senderId, string targetId, string fileName, long fileSize) => new Message
        {
            Type = MessageType.FileUploadStart,
            SenderId = senderId,
            TargetId = targetId,
            FileName = fileName,
            FileSize = fileSize
        };

        public static Message CreateFileChunk(string fileId, byte[] data, long offset, int chunkSize) => new Message
        {
            Type = MessageType.FileChunk,
            FileId = fileId,
            FileData = data,
            FileOffset = offset,
            ChunkSize = chunkSize
        };

        public static Message CreateFileProgress(string fileId, int percent) => new Message
        {
            Type = MessageType.FileProgress,
            FileId = fileId,
            ProgressPercent = percent
        };
    }
}