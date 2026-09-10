using Forest.Models;
using ForestMSG.Core.Enums;
using ForestMSG.Core.Logging;
using ForestMSG.Core.Services.Chatting;
using ForestMSG.Core.Services.ContactManagement;
using ForestMSG.Core.Services.FileSystem;
using ForestMSG.Core.Services.TorrentControl;
using static ForestMSG.Core.Services.Encryption.EncryptionService;

namespace ForestMSG.Core.Services.Messaging
{
    public class MessageSendService
    {
        private readonly MessageEncoder _encoder;
        private readonly ArchiveService _archiveService;
        private readonly TorrentService.MessageTorrentService _torrentService;
        private readonly HandshakeService _handshakeService;
        private readonly ContactService _contactService;
        private readonly string _chatsFolder;

        public MessageSendService(
            MessageEncoder encoder,
            ArchiveService archiveService,
            TorrentService.MessageTorrentService torrentService,
            HandshakeService handshakeService,
            ContactService contactService)
        {
            _encoder = encoder;
            _archiveService = archiveService;
            _torrentService = torrentService;
            _handshakeService = handshakeService;
            _contactService = contactService;

            _chatsFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Chats);
            Directory.CreateDirectory(_chatsFolder);
        }

        public async Task<Message> SendMessageAsync(
            string chatId,
            string text,
            List<string> mediaPaths = null,
            string password = null
        )
        {
            try
            {
                Logger.WriteLog($"[MessageSendService] Отправка сообщения в чат {chatId}");

                var session = _handshakeService.GetSession(chatId);
                if (session == null)
                {
                    throw new InvalidOperationException($"Сессия для чата {chatId} не найдена. Сначала инициируйте чат.");
                }

                var keyPair = await _contactService.LoadKeysAsync(session.SelfId, password ?? "");
                if (keyPair == null)
                {
                    throw new InvalidOperationException($"Не удалось загрузить ключи для {session.SelfId}");
                }

                var message = new Message
                {
                    ChatId = chatId,
                    SenderId = session.SelfId,
                    Type = DetermineMessageType(text, mediaPaths),
                    TextFilePath = string.IsNullOrEmpty(text) ? null : Path.Combine("Text", "content.txt"),
                    SentAt = DateTime.UtcNow,
                    IsDownloaded = false
                };

                string tempFolder = Path.Combine(Path.GetTempPath(), $"msg_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempFolder);

                if (!string.IsNullOrEmpty(text))
                {
                    string textFolder = Path.Combine(tempFolder, "Text");
                    Directory.CreateDirectory(tempFolder);
                    string TextFilePath = Path.Combine(textFolder, "content.txt");
                    await File.WriteAllTextAsync(TextFilePath, text);
                }

                if (mediaPaths != null && mediaPaths.Count > 0)
                {
                    await SaveMediaFilesAsync(tempFolder, mediaPaths, message);
                }

                message.MessageFolderPath = tempFolder;

                var (packet, encryptedMessage) = await _encoder.EncryptMessageAsync(
                    message,
                    session,
                    keyPair.PrivateKey
                );

                string chatFolder = Path.Combine(_chatsFolder, chatId, "Encrypted");
                Directory.CreateDirectory(chatFolder);
                string filePath = _encoder.SaveEncryptedMessageToFile(packet, chatFolder);

                await _torrentService.PublishMessageAsync(filePath, chatId);

                await SaveMessageToHistoryAsync(chatId, encryptedMessage);

                Logger.WriteLog($"[MessageSendService] Сообщение {encryptedMessage} отправлено в чат {chatId}");
                return encryptedMessage;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[MessageSendService] Ошибка отправки {ex.Message}");
                throw;
            }
        }

        private MessageType DetermineMessageType(string text, List<string> mediaPaths)
        {
            bool hasText = !string.IsNullOrEmpty(text);
            bool hasMedia = mediaPaths != null && mediaPaths.Count > 0;

            if (!hasText && !hasMedia)
                throw new ArgumentException("Сообщение должно содержать текст или медиафайлы");

            if (hasText && !hasMedia)
                return MessageType.Text;

            if (hasMedia)
            {
                var mediaTypes = new HashSet<MessageType>();

                foreach (var path in mediaPaths)
                {
                    var ext = Path.GetExtension(path).ToLower();
                    var type = ext switch
                    {
                        ".mp3" or ".wav" or ".ogg" => MessageType.Audio,
                        ".opus" => MessageType.Voice,
                        ".mp4" or ".avi" or ".mov" or ".mkv" or ".webm" => MessageType.Video,
                        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => MessageType.Image,
                        _ => MessageType.Document
                    };
                    mediaTypes.Add(type);
                }

                if (mediaTypes.Count > 1)
                    return MessageType.Multi;

                return mediaTypes.First();
            }

            return MessageType.Text;
        }

        private async Task SaveMediaFilesAsync(string tempFolder, List<string> mediaPaths, Message message)
        {
            message.AudioFiles = new List<string>();
            message.VoicesFiles = new List<string>();
            message.VideoFiles = new List<string>();
            message.PictureFiles = new List<string>();

            foreach (var path in mediaPaths)
            {
                if (!File.Exists(path)) continue;

                var ext = Path.GetExtension(path).ToLower();
                string destFolder;
                string destPath;

                switch (ext)
                {
                    case ".mp3":
                    case ".wav":
                    case ".ogg":
                        destFolder = Path.Combine(tempFolder, "Audio");
                        Directory.CreateDirectory(destFolder);
                        destPath = Path.Combine(destFolder, Path.GetFileName(path));
                        File.Copy(path, destPath, true);
                        message.AudioFiles.Add(destPath);
                        break;

                    case ".opus":
                        destFolder = Path.Combine(tempFolder, "Voices");
                        Directory.CreateDirectory(destFolder);
                        destPath = Path.Combine(destFolder, Path.GetFileName(path));
                        File.Copy(path, destPath, true);
                        message.VoicesFiles.Add(destPath);
                        break;

                    case ".mp4":
                    case ".avi":
                    case ".mov":
                    case ".mkv":
                    case ".webm":
                        destFolder = Path.Combine(tempFolder, "Videos");
                        Directory.CreateDirectory(destFolder);
                        destPath = Path.Combine(destFolder, Path.GetFileName(path));
                        File.Copy(path, destPath, true);
                        message.VideoFiles.Add(destPath);
                        break;

                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                    case ".bmp":
                    case ".webp":
                        destFolder = Path.Combine(tempFolder, "Pictures");
                        Directory.CreateDirectory(destFolder);
                        destPath = Path.Combine(destFolder, Path.GetFileName(path));
                        File.Copy(path, destPath, true);
                        message.PictureFiles.Add(destPath);
                        break;

                    default:
                        destFolder = Path.Combine(tempFolder, "Documents");
                        Directory.CreateDirectory(destFolder);
                        destPath = Path.Combine(destFolder, Path.GetFileName(path));
                        File.Copy(path, destPath, true);
                        message.AudioFiles.Add(destPath);
                        break;
                }
            }
        }

        public async Task<Message> SendTextMessageAsync(string chatId, string text, string password = null)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Текст сообщения не может быть пустым");

            return await SendMessageAsync(chatId, text, null, password);
        }

        public async Task<Message> SendMediaMessageAsync(string chatId, List<string> mediaPaths, string caption = "", string password = null)
        {
            if (mediaPaths == null || mediaPaths.Count == 0)
                throw new ArgumentException("Список медиафайлов не может быть пустым");

            return await SendMessageAsync(chatId, caption, mediaPaths, password);
        }

        public async Task<Message> SendFileMessageAsync(string chatId, string filePath, string caption = "", string password = null)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            return await SendMessageAsync(chatId, caption, new List<string> { filePath }, password);
        }

        private async Task SaveMessageToHistoryAsync(string chatId, Message message)
        {
            try
            {
                string historyFolder = Path.Combine(_chatsFolder, chatId, "History");
                Directory.CreateDirectory(historyFolder);

                string filePath = Path.Combine(historyFolder, $"{message.Id}_{DateTime.UtcNow.Ticks}.json");
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(message, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[MessageSendService] Ошибка сохранения истории: {ex.Message}");
            }
        }
    }
}