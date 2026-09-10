using System.Text.Json;
using Forest.Models;
using ForestMSG.Core.Logging;
using ForestMSG.Core.Services.Chatting;
using ForestMSG.Core.Services.FileSystem;
using ForestMSG.Core.Services.TorrentControl;
using static ForestMSG.Core.Services.Encryption.EncryptionService;

namespace ForestMSG.Core.Services.Messaging
{
    public class MessageReceiveService : IDisposable
    {
        private readonly MessageEncoder _encoder;
        private readonly TorrentService.MessageTorrentService _torrentService;
        private readonly HandshakeService _handshakeService;
        private readonly string _chatsFolder;
        private CancellationTokenSource _cts;
        private Task _listeningTask;
        private bool _isRunning;

        public event Func<Message, Task> MessageReceived;

        public MessageReceiveService(
            MessageEncoder encoder,
            TorrentService.MessageTorrentService torrentService,
            HandshakeService handshakeService
        )
        {
            _encoder = encoder;
            _torrentService = torrentService;
            _handshakeService = handshakeService;
            _chatsFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Chats);
            Directory.CreateDirectory(_chatsFolder);
            _isRunning = false;
        }

        public void StartListening()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            _isRunning = true;
            _listeningTask = Task.Run(ListenLoop);
            Logger.WriteLog("[MessageReceiveService] Запущен прослушивание DHT");
        }

        public async Task StopListeningAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();

            if (_listeningTask != null)
            {
                try
                {
                    await _listeningTask;
                }
                catch (OperationCanceledException)
                {
                    
                }
                _listeningTask = null;
            }

            Logger.WriteLog("[MessageReceiveService] Остановлен");
        }

        private async Task ListenLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await CheckForNewMessages(_cts.Token);
                    await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[MessageReceiveService] Ошибка в цикле: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
                }
            }
        }

        private async Task CheckForNewMessages(CancellationToken cancellationToken)
        {
            var sessions = _handshakeService.GetAllSessions();
            if (sessions == null || sessions.Count == 0) return;

            foreach (var session in sessions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await CheckChatMessages(session, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[MessageReceiveService] Ошибка чата {session.ChatId}: {ex.Message}");
                }
            }
        }

        private async Task CheckChatMessages(ChatSession session, CancellationToken cancellationToken)
        {
            string chatEncryptedFolder = Path.Combine(_chatsFolder, session.ChatId, "Encrypted");
            if (!Directory.Exists(chatEncryptedFolder)) return;

            var encFiles = Directory.GetFiles(chatEncryptedFolder, "*.enc")
            .Where(f => !IsMessageProcessed(f))
            .ToList();

            if (encFiles.Count == 0) return;

            Logger.WriteLog($"[MessageReceiveService] Найдено {encFiles.Count} новых сообщений в чате {session.ChatId}");

            foreach (var filePath in encFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await ProcessMessageFile(filePath, session);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[MessageReceiveService] Ошибка обработки {filePath}: {ex.Message}");
                }
            }
        }

        private async Task ProcessMessageFile(string filePath, ChatSession session)
        {
            var message = await _encoder.LoadAndDecryptMessageFromFileAsync(filePath, session);

            if (message == null)
            {
                Logger.WriteLog($"[MessageReceiveService] Не удалось расшифровать {filePath}");
                MarkMessageAsProcessed(filePath, false);
                return;
            }

            if (message.ChatId != session.ChatId)
            {
                Logger.WriteLog($"[MessageReceiveService] Чат не совпадает: {message.ChatId} != {session.ChatId}");
                return;
            }

            await SaveMessageToHistoryAsync(session.ChatId, message);

            MarkMessageAsProcessed(filePath, true);

            File.Delete(filePath);

            if (MessageReceived != null)
            {
                await MessageReceived.Invoke(message);
            }

            Logger.WriteLog($"[MessageReceiveService] Сообщение {message.Id} получено в чате {session.ChatId}");
        }

        private readonly HashSet<string> _processedFiles = new HashSet<string>();

        private bool IsMessageProcessed(string filePath)
        {
            return _processedFiles.Contains(filePath);
        }

        private void MarkMessageAsProcessed(string filePath, bool success)
        {
            if (success)
            {
                _processedFiles.Add(filePath);
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            else
            {
                if (File.Exists(filePath))
                {
                    Logger.WriteLog($"[MessageReceiveService] Удаление повреждённого файла: {filePath}");
                    File.Delete(filePath);
                }
            }
        }

        private async Task SaveMessageToHistoryAsync(string chatId, Message message)
        {
            try
            {
                string historyFolder = Path.Combine(_chatsFolder, chatId, "History");
                Directory.CreateDirectory(historyFolder);

                string filePath = Path.Combine(historyFolder, $"{message.Id}_{DateTime.UtcNow.Ticks}.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(message, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[MessageReceiveService] Ошибка сохранения истории: {ex.Message}");
            }
        }

        public async Task CheckNowAsync()
        {
            await CheckForNewMessages(CancellationToken.None);
        }

        public async void Dispose()
        {
            await StopListeningAsync();
            _cts?.Dispose();
        }
    }
}