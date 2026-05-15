namespace MessengerProtocol
{
    public enum MessageType
    {
        // Служебные сообщения
        Handshake,      // Подключение клиента
        UserList,       // Список активных пользователей
        UserConnected,  // Пользователь подключился
        UserDisconnected, // Пользователь отключился
        Status,         // Статус операции
        
        // Текстовые сообщения
        BroadcastChat,  // Публичное сообщение
        PrivateChat,    // Личное сообщение
        
        // Файловые операции
        FileUploadStart,    // Начало загрузки файла
        FileChunk,          // Фрагмент файла
        FileUploadComplete, // Завершение загрузки
        FileDownloadRequest, // Запрос на скачивание
        FileInfo,           // Информация о файле
        FileProgress        // Прогресс передачи
    }
}