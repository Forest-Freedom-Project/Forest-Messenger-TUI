using System.Text.Json;
using Forest.Models;
using ForestMSG.Core.Logging;
using ForestMSG.Core.Models;
using ForestMSG.Core.Services.ContactManagement;
using ForestMSG.Core.Services.FileSystem;
using ForestMSG.Core.Services.Messaging;
using static ForestMSG.Core.Services.Encryption.EncryptionService;

namespace ForestMSG.Core.Services.Chatting
{
    public class ChatService
    {
        private readonly HandshakeService _handshakeService;
        private readonly MessageSendService _sendService;
        private readonly MessageReceiveService _receiveService;
        private readonly ContactService _contactService;
        private readonly string _chatsFolder;

        public ChatService(
            HandshakeService handshakeService,
            MessageSendService sendService,
            MessageReceiveService receiveService,
            ContactService contactService
        )
        {
           _handshakeService = handshakeService;
           _sendService = sendService;
           _receiveService = receiveService;
           _contactService = contactService;
           _chatsFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Chats);
           Directory.CreateDirectory(_chatsFolder); 
        }

        public async Task<Chat> CreateChatAsync(
            string peerId,
            string myPublicId,
            byte[] myPrivateKey,
            byte[] myEncryptionPrivateKey
        )
        {
            var peerContact = await _contactService.LoadContactAsync(peerId);
            if (peerContact == null) throw new Exception($"Контакт {peerId} не найден");

            var chatId = await _handshakeService.InitialiteChatAsync(
                peerId,
                myPrivateKey,
                myEncryptionPrivateKey,
                myPublicId
            );

            var chat = new Chat
            {
                Id = chatId,
                SelfId = myPublicId,
                PeerId = peerId,
                PeerName = peerContact.Name ?? peerId,
                Messages = new List<Message>()          
            };

            await SaveChatAsync(chat);

            Logger.WriteLog($"[ChatService] Чат создан: {chatId} с {peerId}");
            return chat;
        }

        public async Task<Chat?> LoadChatAsync(string chatId)
        {
            string chatFolder = Path.Combine(_chatsFolder, chatId);
            string metadataPath = Path.Combine(chatFolder, "metadata.json");

            if (!File.Exists(metadataPath)) return null;

            string json = await File.ReadAllTextAsync(metadataPath);
            return JsonSerializer.Deserialize<Chat>(json);
        }

        public async Task<List<Chat>> LoadAllChatsAsync()
        {
            var chats = new List<Chat>();

            if (!Directory.Exists(_chatsFolder)) return chats;

            foreach (var chatFolder in Directory.GetDirectories(_chatsFolder))
            {
                var chatId = Path.GetFileName(chatFolder);
                var chat = await LoadChatAsync(chatId);
                if (chat != null) chats.Add(chat);
            }

            return chats;
        }

        private async Task SaveChatAsync(Chat chat)
        {
            string chatFolder = Path.Combine(_chatsFolder, chat.Id);
            Directory.CreateDirectory(chatFolder);

            string metadataPath = Path.Combine(chatFolder, "metadata.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(chat, options);
            await File.WriteAllTextAsync(metadataPath, json);
        }

        public async Task DeleteChatAsync(string chatId)
        {
            string chatFolder = Path.Combine(_chatsFolder, chatId);
            if (Directory.Exists(chatFolder))
            {
                _handshakeService.RemoveSession(chatId);

                Directory.Delete(chatFolder, true);
                Logger.WriteLog($"[ChatService] Чат {chatId} удалён");
            }
            await Task.CompletedTask;
        }

        public async Task<Message> SendTextMessageAsync(string chatId, string text, string password)
        {
            return await _sendService.SendTextMessageAsync(chatId, text, password);
        }

        public async Task<Message> SendMediaMessageAsync(string chatId, List<string> mediaPaths, string caption, string password)
        {
            return await _sendService.SendMediaMessageAsync(chatId, mediaPaths, caption, password);
        }

        public async Task<Message> SendFileMessageAsync(string chatId, string filePath, string caption, string password)
        {
            return await _sendService.SendFileMessageAsync(chatId, filePath, caption, password);
        }

        public void StartReceiving()
        {
            if (_receiveService != null)
            {
                _receiveService.StartListening();
                Logger.WriteLog("[ChatService] Приём сообщений запущен");
            }
        }

        public async Task StopReceivingAsync()
        {
            if (_receiveService != null)
            {
                await _receiveService.StopListeningAsync();
                Logger.WriteLog("[ChatService] Приём сообщений остановлен");
            }
        }

        public async Task CheNowAsync()
        {
            if (_receiveService != null)
            {
                await _receiveService.CheckNowAsync();
            }
        }

        public void OnMessageReceived(Func<Message, Task> handler)
        {
            if (_receiveService != null)
            {
                _receiveService.MessageReceived += handler;
            }
        }

        public async Task<List<Message>> LoadHistoryAsync(string chatId)
        {
            var messages = new List<Message>();
            string historyFolder = Path.Combine(_chatsFolder, chatId, "History");

            if (!Directory.Exists(historyFolder)) return messages;

            foreach (var filePath in Directory.GetFiles(historyFolder, "*.json"))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    var message = JsonSerializer.Deserialize<Message>(json);
                    if (message != null) messages.Add(message);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[ChatService] Ошибка загрузки {filePath}: {ex.Message}");
                }
            }

            return messages.OrderBy(m => m.SentAt).ToList();
        }

        public async Task<List<Chat>> SearchCharsAsync(string query)
        {
            var chats = await LoadAllChatsAsync();
            return chats.Where(c => c.PeerName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        public ChatSession? GetSession(string chatId)
        {
            return _handshakeService.GetSession(chatId);
        }

        public bool HasSession(string chatId)
        {
            return _handshakeService.HasSession(chatId);
        }
    }
}