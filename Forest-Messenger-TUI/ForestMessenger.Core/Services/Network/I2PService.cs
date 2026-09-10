using System.Net;
using System.Net.Sockets;
using DotI2p;
using ForestMSG.Core.Logging;

namespace ForestMSG.Core.Services.Network
{
    public class I2PService : IDisposable
    {
        private SamConnection _connection;
        private SamSession _session;
        private bool _isConnected;
        private readonly string _samHost;
        private readonly int _samTcpPort;
        private readonly int _samUdpPort;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        public string B32Address { get; private set; }

        public string DestinationBase64 { get; private set; }

        public event EventHandler<bool> ConnectionStateChanged;

        public I2PService(string samHost = "127.0.0.1", int samTcpPort = 7656, int samUdpPort = 7655)
        {
            _samHost = samHost;
            _samTcpPort = samTcpPort;
            _samUdpPort = samUdpPort;
            _isConnected = false;
        }        

        public async Task<bool> ConnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_isConnected)
                { 
                    Logger.WriteLog("[I2PService] Уже подключён, пропускаем");
                    return true;
                }

                Logger.WriteLog("[I2PService] Подключение к SAM bridge...");
                
                _connection = new SamConnection(
                    IPAddress.Parse(_samHost),
                    tcpPort: _samTcpPort,
                    udpPort: _samUdpPort
                );

                await _connection.ConnectAsync();

                _session = new SamSession(_connection);

                var destionation = await _session.CreatePrimarySessionAsync();

                B32Address = destionation.GetB32Hostname();
                DestinationBase64 = destionation.Destination;

                _isConnected = true;
                ConnectionStateChanged?.Invoke(this, true);

                Logger.WriteLog($"[I2PService] Подключено к I2P. Адрес: {B32Address}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[I2PService] Ошибка подключения: {ex.Message}");
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task<SamStreamSubSession> CreateStreamSubSessionAsync()
        {
            EnsureConnected();
            return await _session.CreateStreamSubSession();
        }

        public async Task<TcpClient> ConnectToDestinationAsync(string b32Address, int remotePost = 0)
        {
            EnsureConnected();

            var remote = await _session.HostNameLookupAsync(b32Address);
            var streamSubSession = await CreateStreamSubSessionAsync();
            var virtualStream = streamSubSession.CreateVirtualStream();

            return await virtualStream.ConnectAsync(remote);
        }

        public async Task<SamVirtualStream> CreateListeningStreamAsync()
        {
            EnsureConnected();
            var streamSubSession = await CreateStreamSubSessionAsync();
            return streamSubSession.CreateVirtualStream();
        }

        public async Task<dynamic> LookupDestinationAsync(string b32Address)
        {
            EnsureConnected();
            return await _session.HostNameLookupAsync(b32Address);
        }

        private void EnsureConnected()
        {
            if (!_isConnected)
            { throw new InvalidOperationException("I2PService не подключён. Вызовите ConnectAsync() перед использованием."); }
        }

        public bool IsConnected => _isConnected;

        public async Task ReconnectAsync()
        {
            if (_isConnected)
            {
                await DisconnectAsync();
            }
            await ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_connection != null)
                {
                    _connection.Dispose();
                    _connection = null;
                }
                _session = null;
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
                Logger.WriteLog("[I2PService] Отключён от I2P");
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connectionLock?.Dispose();
        }
    }
}