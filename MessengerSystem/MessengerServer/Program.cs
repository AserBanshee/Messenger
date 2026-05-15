using System;
using System.Threading.Tasks;

namespace MessengerServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Messenger TCP Server ===");
            Console.Write("Введите порт (по умолчанию 8888): ");
            string portInput = Console.ReadLine();
            int port = string.IsNullOrEmpty(portInput) ? 8888 : int.Parse(portInput);
            
            var server = new Server();
            server.OnLog += (msg) => Console.WriteLine(msg);
            
            Console.WriteLine("Запуск сервера...");
            await server.StartAsync(port);
            
            Console.WriteLine("Нажмите Enter для остановки сервера...");
            Console.ReadLine();
            server.Stop();
        }
    }
}