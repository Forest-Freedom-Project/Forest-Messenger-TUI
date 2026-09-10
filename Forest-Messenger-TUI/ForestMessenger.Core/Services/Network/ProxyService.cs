using ForestMSG.Core.Logging;

namespace ForestMSG.Core.Services.Network
{
    public class ProxyService : IDisposable
    {
        private readonly I2PService _i2pService;
        private readonly TorService _torService;

        public bool IsI2PAvailable => _i2pService?.IsConnected ?? false;

        public bool IsTorAvailable { get; private set; }

        public ProxyService(I2PService i2PService, TorService torService)
        {
            _i2pService = i2PService;
            _torService = torService;
        }

        public async Task InitializeAsync()
        {
            Logger.WriteLog("[ProxyService] Инициализация...");

            try
            {
                await _i2pService.ConnectAsync();
                Logger.WriteLog("[ProxyService] I2P подключён");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ProxyService] Ошибка I2P: {ex.Message}");
            }

            try
            {
                await _torService.StartAsync();
                IsTorAvailable = await _torService.IsTorAvailableAsync();
                Logger.WriteLog($"[ProxyService] Tor доступен: {IsTorAvailable}");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ProxyService] Ошибка Tor: {ex.Message}");
            }

            Logger.WriteLog("[ProxyService] Инициализация завершена");
        }

        public I2PService GetI2PService()
        {
            if (!IsI2PAvailable)
            {
                throw new InvalidOperationException(
                    "I2P недоступен. Внутренний трафик Forest невозможен без I2P."
                );
            }
            return _i2pService;
        }

        public HttpClient CreateI2PHttpClient()
        {
            if (!IsI2PAvailable)
                throw new InvalidOperationException("I2P недоступен");

            var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy("http://127.0.0.1:4444"),
                UseProxy = true
            };
            return new HttpClient(handler);
        }

        public TorService GetTorService()
        {
            if (!IsTorAvailable)
            {
                throw new InvalidOperationException(
                    "Tor недоступен. Внешние запросы Forest невозможны без Tor."
                );
            }
            return _torService;
        }

        public HttpClient CreateTorHttpClient()
        {
            if (!IsTorAvailable)
            { throw new InvalidOperationException("Tor недоступен"); }

            return _torService.CreateHttpClient();
        }

        public async Task<string?> GetExternalAsync(string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var client = CreateTorHttpClient();
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex) when (i < maxRetries - 1)
                {
                    Logger.WriteLog($"[ProxyService] Попытка {i + 1} не удалась: {ex.Message}");
                    await Task.Delay(2000);
                }
            }
            return null;
        }

        public ProxyStatus GetStatus()
        {
            return new ProxyStatus
            {
                I2PConnected = IsI2PAvailable,
                TorAvailable = IsTorAvailable
            };
        }

        public void Dispose()
        {
            _i2pService?.Dispose();
            _torService?.Dispose();
        }

    }
    public class ProxyStatus
    {
        public bool I2PConnected { get; set; }
        public bool TorAvailable { get; set; }
    }
}