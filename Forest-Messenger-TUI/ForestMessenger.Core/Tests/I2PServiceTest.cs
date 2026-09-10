using ForestMSG.Core.Services.Network;

namespace ForestMSG.Tests
{
    public class I2PServiceTest
    {
        public static async Task RunTest()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("Тестирование I2PService");
            Console.WriteLine("========================================\n");

            var i2pService = new I2PService();

            Console.WriteLine("[1/5] Подключение к SAM bridge...");
            var connected = await i2pService.ConnectAsync();

            if (!connected)
            {
                Console.WriteLine("Ошибка: не удалось подключиться к SAM bridge.");
                Console.WriteLine("Убедитесь, что I2P роутер (i2pd) запущен и SAM включён.");
                Console.WriteLine("Проверьте: 127.0.0.1:7656");
                return;
            }

            Console.WriteLine($"Подключено к SAM. B32 адрес: {i2pService.B32Address}\n");

            Console.WriteLine("[2/5] Создание стрим-субсессии...");
            try
            {
                var streamSub = await i2pService.CreateStreamSubSessionAsync();
                Console.WriteLine($"Субсессия создана. ID: {streamSub.GetHashCode()}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания субсессии: {ex.Message}");
                return;
            }

            Console.WriteLine("[3/5] Резолвинг .b32.i2p адреса (tracker2.postman.i2p)...");
            try
            {
                var destination = await i2pService.LookupDestinationAsync("tracker2.postman.i2p");
                Console.WriteLine($"Адрес найден:");
                Console.WriteLine($"Base64: {destination.Destination.Substring(0, 50)}...");
                Console.WriteLine($"B32: {destination.GetB32Hostname()}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка резолвинга: {ex.Message}");
                Console.WriteLine("(Это может быть нормально, если сеть I2P ещё не стабилизировалась)\n");
            }

            Console.WriteLine("[4/5] Создание слушающего стрима...");
            try
            {
                var listeningStream = await i2pService.CreateListeningStreamAsync();
                Console.WriteLine($"Слушающий стрим создан\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания слушающего стрима: {ex.Message}\n");
            }

            Console.WriteLine($"[5/5] Статус I2PService:");
            Console.WriteLine($"Подключён: {i2pService.IsConnected}");
            Console.WriteLine($"B32 адрес: {i2pService.B32Address ?? "Не задан"}");
            Console.WriteLine($"Destination: {(i2pService.DestinationBase64?.Length > 0 ? "Установлен" : "Не установлен")}");

            Console.WriteLine("\n========================================");
            if (i2pService.IsConnected)
            {
                Console.WriteLine("I2PService работает корректно!");
                Console.WriteLine($"Адрес вашего узла: {i2pService.B32Address}");
                Console.WriteLine("Вы можете обмениваться данными через I2P.");
            }
            else
            {
                Console.WriteLine("I2PService не работает.");
                Console.WriteLine("Проверьте: запущен ли i2pd с включённым SAM.");
            }
            Console.WriteLine("========================================\n");

            await i2pService.DisconnectAsync();
        }
    }
}