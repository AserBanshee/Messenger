using System;
using System.Text;
using System.Text.Json;

namespace MessengerProtocol
{
    public static class MessageSerializer
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Сериализация с префиксом длины
        public static byte[] Serialize(Message message)
        {
            string json = JsonSerializer.Serialize(message, _options);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] lengthPrefix = BitConverter.GetBytes(jsonBytes.Length);
            
            byte[] result = new byte[lengthPrefix.Length + jsonBytes.Length];
            Buffer.BlockCopy(lengthPrefix, 0, result, 0, lengthPrefix.Length);
            Buffer.BlockCopy(jsonBytes, 0, result, lengthPrefix.Length, jsonBytes.Length);
            
            return result;
        }

        // Десериализация из потока
        public static Message Deserialize(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<Message>(json, _options);
        }
    }
}