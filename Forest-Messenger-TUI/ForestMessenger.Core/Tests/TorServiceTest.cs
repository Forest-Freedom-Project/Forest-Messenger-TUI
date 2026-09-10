using ForestMSG.Core.Services.Network;

namespace ForestMSG.Tests
{
    public class TorServiceTest
    {
        public static async Task RunTest()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Тестирование TorService");
            Console.WriteLine("========================================\n");

            var torService = new TorService();

            Console.WriteLine("[1/5] Проверка доступности системного Tor...");
            bool isAvailable = await torService.IsTorAvailableAsync();

            if (!isAvailable)
            {
                Console.WriteLine("Tor не запущен на 127.0.0.1:9050.");
                Console.WriteLine("Запустите системный Tor:");
                Console.WriteLine("  Linux:   sudo systemctl start tor");
                Console.WriteLine("  macOS:   brew services start tor");
                Console.WriteLine("  Windows: запустите Tor Expert Bundle\n");
                return;
            }

            Console.WriteLine("Системный Tor доступен на 127.0.0.1:9050\n");

            Console.WriteLine("[2/5] Подключение к системному Tor...");
            try
            {
                await torService.StartAsync();
                Console.WriteLine("Подключено к Tor\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения: {ex.Message}\n");
                return;
            }

            Console.WriteLine("[3/5] Создание HTTP-клиента через Tor...");
            try
            {
                using var client = torService.CreateHttpClient();
                Console.WriteLine("HTTP-клиент создан\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания HTTP-клиента: {ex.Message}\n");
                return;
            }

            Console.WriteLine("[4/5] Запрос к check.torproject.org...");
            try
            {
                string html = await torService.GetAsync("https://check.torproject.org");

                var ipMatch = System.Text.RegularExpressions.Regex.Match(
                    html,
                    @"<strong>(\d+\.\d+\.\d+\.\d+)</strong>"
                );

                if (ipMatch.Success)
                {
                    string ip = ipMatch.Groups[1].Value;
                    Console.WriteLine($"Запрос выполнен через Tor. Ваш IP: {ip}");

                    if (ip.StartsWith("10.") || ip.StartsWith("192.168.") || ip.StartsWith("172.16."))
                    {
                        Console.WriteLine("IP выглядит как локальный. Возможно, Tor не работает корректно.\n");
                    }
                    else
                    {
                        Console.WriteLine("IP не локальный. Tor работает корректно!\n");
                    }
                }
                else
                {
                    Console.WriteLine("Не удалось найти IP в ответе, но запрос выполнен успешно.\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запроса: {ex.Message}");
                Console.WriteLine("Проверьте соединение с интернетом.\n");
            }

            Console.WriteLine("[5/5] Статус TorService:");
            Console.WriteLine($"  Tor доступен: {await torService.IsTorAvailableAsync()}");
            Console.WriteLine($"  SOCKS5 порт: 9050");
            Console.WriteLine($"  Режим: подключение к системному Tor");

            Console.WriteLine("\n========================================");
            if (await torService.IsTorAvailableAsync())
            {
                Console.WriteLine("TorService работает корректно!");
                Console.WriteLine("Вы можете делать анонимные запросы в Clearnet.");
            }
            else
            {
                Console.WriteLine("TorService не работает.");
                Console.WriteLine("Проверьте: запущен ли системный Tor на порту 9050.");
            }
            Console.WriteLine("========================================\n");

            await torService.StopAsync();
        }
    }
}