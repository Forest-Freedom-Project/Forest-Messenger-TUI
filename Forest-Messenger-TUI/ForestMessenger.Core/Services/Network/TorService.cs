using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ForestMSG.Core.Logging;

namespace ForestMSG.Core.Services.Network
{
    public class TorService : IDisposable
    {
        private bool _isRunning;
        private readonly string _proxyHost = "127.0.0.1";
        private readonly int _proxyPort = 9050;
        private readonly string _os;

        public TorService()
        {
            _os = GetOperatingSystem();
        }

        private string GetOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "Windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "Linux";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "macOS";
            return "Unknown";
        }

        public async Task StartAsync()
        {
            try
            {
                Logger.WriteLog($"[TorService] Подключение к системному Tor на {_os}...");

                bool isAvailable = await IsTorAvailableAsync();

                if (!isAvailable)
                {
                    throw new Exception(GetTorStartInstructions());
                }

                _isRunning = true;
                Logger.WriteLog("[TorService] Подключён к системному Tor");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[TorService] Ошибка: {ex.Message}");
                _isRunning = false;
                throw;
            }
        }

        private string GetTorStartInstructions()
        {
            return _os switch
            {
                "Linux" =>
                    "Tor не запущен на 127.0.0.1:9050.\n" +
                    "Запустите системный Tor:\n" +
                    "  sudo systemctl start tor\n" +
                    "  sudo systemctl enable tor",

                "Windows" =>
                    "Tor не запущен на 127.0.0.1:9050.\n" +
                    "Запустите Tor Expert Bundle:\n" +
                    "  1. Скачайте с https://www.torproject.org/download/tor/\n" +
                    "  2. Распакуйте в C:\\Tor\\\n" +
                    "  3. Запустите tor.exe",

                "macOS" =>
                    "Tor не запущен на 127.0.0.1:9050.\n" +
                    "Запустите системный Tor:\n" +
                    "  brew install tor\n" +
                    "  brew services start tor",

                _ => "Tor не запущен на 127.0.0.1:9050. Установите и запустите Tor."
            };
        }

        public async Task<bool> IsTorAvailableAsync()
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_proxyHost, _proxyPort);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public HttpClient CreateHttpClient()
        {
            if (!_isRunning)
                throw new InvalidOperationException("Tor не подключён. Вызовите StartAsync() сначала.");

            var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://{_proxyHost}:{_proxyPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            return client;
        }

        public async Task<string> GetAsync(string url)
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public Task StopAsync()
        {
            _isRunning = false;
            Logger.WriteLog("[TorService] Отключён от Tor");
            return Task.CompletedTask;
        }

        public async Task ReconnectAsync()
        {
            await StopAsync();
            await Task.Delay(1000);
            await StartAsync();
        }

        public async Task AddBridgesAsync(List<string> bridges)
        {
            try
            {
                Logger.WriteLog($"[TorService] Добавление {bridges.Count} мостов...");

                string torrcPath = GetTorrcPath();

                var config = new List<string>();
                if (File.Exists(torrcPath))
                {
                    config = File.ReadAllLines(torrcPath).ToList();
                }

                config = config.Where(line => !line.StartsWith("Bridge ")
                                           && !line.StartsWith("UseBridges ")).ToList();

                config.Add("UseBridges 1");
                foreach (var bridge in bridges)
                {
                    config.Add($"Bridge {bridge}");
                }

                if (OperatingSystem.IsLinux())
                {
                    config.Add("ClientTransportPlugin obfs4 exec /usr/bin/obfs4proxy");
                }
                else if (OperatingSystem.IsWindows())
                {
                    config.Add("ClientTransportPlugin obfs4 exec C:\\Tor\\obfs4proxy.exe");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    config.Add("ClientTransportPlugin obfs4 exec /usr/local/bin/obfs4proxy");
                }

                // 6. Записываем
                await File.WriteAllLinesAsync(torrcPath, config);

                Logger.WriteLog($"[TorService] Мосты добавлены в {torrcPath}");

                await RestartSystemTorAsync();
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[TorService] Ошибка добавления мостов: {ex.Message}");
                throw;
            }
        }

        private string GetTorrcPath()
        {
            if (OperatingSystem.IsLinux())
                return "/etc/tor/torrc";
            if (OperatingSystem.IsWindows())
                return @"C:\Users\Default\AppData\Roaming\tor\torrc";
            if (OperatingSystem.IsMacOS())
                return "/usr/local/etc/tor/torrc";
            throw new PlatformNotSupportedException();
        }

        private async Task RestartSystemTorAsync()
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    await RunCommandAsync("sudo", "systemctl restart tor");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    await RunCommandAsync("brew", "services restart tor");
                }
                else if (OperatingSystem.IsWindows())
                {
                    await RunCommandAsync("net", "stop tor");
                    await RunCommandAsync("net", "start tor");
                }

                Logger.WriteLog("[TorService] Tor перезапущен");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[TorService] Ошибка перезапуска Tor: {ex.Message}");
                throw;
            }
        }

        private async Task RunCommandAsync(string command, string args)
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Команда '{command} {args}' завершилась с ошибкой: {error}");
            }
        }

        public void Dispose()
        {
            _isRunning = false;
        }
    }
}