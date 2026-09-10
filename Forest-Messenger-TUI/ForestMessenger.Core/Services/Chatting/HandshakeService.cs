using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForestMSG.Core.Models;
using ForestMSG.Core.Services.TorrentControl;
using ForestMSG.Core.Logging;
using static ForestMSG.Core.Services.Encryption.EncryptionService;
using ForestMSG.Core.Services.ContactManagement;

namespace ForestMSG.Core.Services.Chatting
{
    public class HandshakeService : IDisposable
    {
        private readonly TorrentService.ContactTorrentService _torrentService;
        private readonly ContactService _contactService;
        private readonly ConcurrentDictionary<string, ChatSession> _sessions;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _handshakeTimers;
        private CancellationTokenSource? _listenerCts;
        private bool _isListening;

        public event Func<string, string, Task<bool>>? HandshakeRequested;

        public HandshakeService(
            TorrentService.ContactTorrentService torrentService,
            ContactService contactService
        )
        {
            _torrentService = torrentService;
            _contactService = contactService;
            _sessions = new ConcurrentDictionary<string, ChatSession>();
            _handshakeTimers = new ConcurrentDictionary<string, CancellationTokenSource>();
            _isListening = false;
        }

        public async Task<string> InitialiteChatAsync(
            string peerId,
            byte[] myPrivateKey,
            byte[] myEncryptionPrivateKey,
            string myPublicId
        )
        {
            var peerContact = await _contactService.LoadContactAsync(peerId);
            if(peerContact == null)
            {
                throw new Exception($"Контакт {peerId} не найден локально. Сначала найдите его через DHT.");
            }

            byte[] peerEncryptionKey = Convert.FromBase64String(peerContact.EncryptionKey);
            byte[] sharedSecret = CryptoKeysGenerator.ComputeSharedSecret(myEncryptionPrivateKey, peerEncryptionKey);

            string chatId = GenerateChatId(myPublicId, peerId);
            byte[] chatSalt = DeriveChatSalt(myPublicId, peerId);

            byte[] rootKey = DeriveRootKey(sharedSecret, chatSalt);

            var handshake = new HandshakePacket
            {
                ChatId = chatId,
                InitiatorId = myPublicId,
                RecipientId = peerId,
                EncryptedRootKey = EncryptForRecipient(rootKey, peerEncryptionKey),
                EncryptedChatSalt = EncryptForRecipient(chatSalt, peerEncryptionKey),

                CreatedAt = DateTime.UtcNow,
                TTL = 300
            };

            byte[] handshakeData = JsonSerializer.SerializeToUtf8Bytes(handshake);
            handshake.Signature = CryptoKeysGenerator.SignData(handshakeData, myPrivateKey);

            await _torrentService.PublishHandshakeAsync(handshake);

            var session = new ChatSession
            {
                ChatId = chatId,
                RootKey = rootKey,
                ChatSalt = chatSalt,
                SelfId = myPublicId,
                PeerId = peerId,
                PeerPublicKey = Convert.FromBase64String(peerContact.PublicKey),
                NextMessageId = 1
            };
            _sessions[chatId] = session;

            _ = ScheduleHandshakeRemovalAsync(chatId);

            Logger.WriteLog($"[Handshake] Чат инициирован: {chatId}");

            return chatId;
        }

        public async Task StartListeningAsync(string myPublicId, byte[] myPrivateKey, byte[] myEncryptionPrivateKey)
        {
            if(_isListening)
            {
                return;
            }

            _listenerCts = new CancellationTokenSource();
            _isListening = true;

            await Task.Run(async () =>
            {
                while(!_listenerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ScanForHandshakeAsync(myPublicId, myPrivateKey, myEncryptionPrivateKey);
                        await Task.Delay(TimeSpan.FromSeconds(10), _listenerCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog($"[Handshake] Ошибка сканирования: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }
                }
            });
        }

        public void StopListening()
        {
            _isListening = false;
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
        }

        private async Task ScanForHandshakeAsync(string myPublicId, byte[] myPrivateKey, byte[] myEncryptionPrivateKey)
        {
            var handshakes = await _torrentService.FindHandshakesForMeAsync(myPublicId);
            if(handshakes == null || !handshakes.Any())
            { return; }

            foreach (var handshake in handshakes)
            {
                await ProcessHandshakeAsync(handshake, myPublicId, myPrivateKey, myEncryptionPrivateKey);
            }
        }

        private async Task ProcessHandshakeAsync(
            HandshakePacket handshake,
            string myPublicId,
            byte[] myPrivateKey,
            byte[] myEncryptionPrivateKey)
        {
            try
            {
                if(handshake.IsExpired())
                {
                    Logger.WriteLog($"[Handshake] Рукопожатие {handshake.ChatId} просрочено");
                    return;
                }

                var initiatorContact = await _contactService.LoadContactAsync(handshake.InitiatorId);
                if (initiatorContact == null)
                {
                    Logger.WriteLog($"[Handshake] Контакт инициатора не найден: {handshake.InitiatorId}");
                    return;
                }

                byte[] handshakeData = JsonSerializer.SerializeToUtf8Bytes(handshake);
                bool isValid = CryptoKeysGenerator.VerifySignature(
                    handshakeData,
                    handshake.Signature,
                    Convert.FromBase64String(initiatorContact.PublicKey)
                );

                if(!isValid)
                {
                    Logger.WriteLog($"[Handshake] Неверная подпись от {handshake.InitiatorId}");
                    return;
                }

                byte[] peerEncryptionKey = Convert.FromBase64String(initiatorContact.EncryptionKey);
                byte[] rootKey = DecryptForRecipient(handshake.EncryptedRootKey, myEncryptionPrivateKey);
                byte[] chatSalt = DecryptForRecipient(handshake.EncryptedChatSalt, myEncryptionPrivateKey);

                if(_sessions.ContainsKey(handshake.ChatId))
                {
                    Logger.WriteLog($"[Handshake] Сессия {handshake.ChatId} уже существует");
                    return;
                }

                var session = new ChatSession
                {
                    ChatId = handshake.ChatId,
                    RootKey = rootKey,
                    ChatSalt = chatSalt,
                    SelfId = myPublicId,
                    PeerId = handshake.InitiatorId,
                    PeerPublicKey = Convert.FromBase64String(initiatorContact.PublicKey),
                    NextMessageId = 1                    
                };

                bool accepted = false;
                if(HandshakeRequested != null)
                {
                    accepted = await HandshakeRequested.Invoke(handshake.InitiatorId, handshake.ChatId);
                }

                if(accepted)
                {
                    _sessions[handshake.ChatId] = session;
                    await SendConfirmationAsync(handshake.ChatId, handshake.InitiatorId, myPrivateKey);
                    
                    Logger.WriteLog($"[Handshake] Чат {handshake.ChatId} принят");
                }
                else
                {
                    Logger.WriteLog($"[Handshake] Чат {handshake.ChatId} отклонён");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Handshake] Ошибка обработки: {ex.Message}");
            }
        }

        private async Task SendConfirmationAsync(string chatId, string initiatorId, byte[] myPrivateKey)
        {
            var confirmation = new HandshakeConfirmation
            {
                ChatId = chatId,
                RecipientId = initiatorId,
                ConfirmedAt = DateTime.UtcNow                
            };

            byte[] data = JsonSerializer.SerializeToUtf8Bytes(confirmation);
            confirmation.Signature = CryptoKeysGenerator.SignData(data, myPrivateKey);

            await _torrentService.PublishConfirmationAsync(confirmation);
        }

        private async Task ScheduleHandshakeRemovalAsync(string chatId)
        {
            var cts = new CancellationTokenSource();
            _handshakeTimers[chatId] = cts;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);

                if(!_sessions.ContainsKey(chatId))
                {
                    await _torrentService.RemoveHandshakeFromDHTAsync(chatId);
                    Logger.WriteLog($"[Handshake] Рукопожатие {chatId} удалено (таймаут)");
                }
            }
            catch(TaskCanceledException)
            {
                
            }
        }

        public ChatSession? GetSession(string chatId)
        {
            _sessions.TryGetValue(chatId, out var session);
            return session;
        }

        public List<ChatSession> GetAllSessions()
        {
            return new (_sessions.Values);
        }

        public void RemoveSession(string chatId)
        {
            if (_sessions.TryRemove(chatId, out _))
            {
                Logger.WriteLog($"[HandshakeService] Сессия {chatId} удалена");
            }
        }

        public bool HasSession(string chatId) => _sessions.ContainsKey(chatId);

        private byte[] DecryptForRecipient(byte[] encryptedData, byte[] privateKey)
        {
            using var sha = SHA256.Create();
            byte[] key = sha.ComputeHash(privateKey);

            byte[] nonce = new byte[12];
            byte[] ciphertext = new byte[encryptedData.Length - 12 - 16];
            byte[] tag = new byte[16];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, 12);
            Buffer.BlockCopy(encryptedData, 12, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(encryptedData, 12 + ciphertext.Length, tag, 0, 16);

            byte[] plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }

        private byte[] EncryptForRecipient(byte[] data, byte[] publicKey)
        {
            using var sha = SHA256.Create();
            byte[] key = sha.ComputeHash(publicKey);

            byte[] nonce = new byte[12];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(nonce);

            byte[] ciphertext = new byte[data.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(key);
            aes.Encrypt(nonce, data, ciphertext, tag);

            byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

            return result;
        }

        private byte[] DeriveRootKey(byte[] sharedSecret, byte[] chatSalt)
        {
            using var hkdf = new HMACSHA256(sharedSecret);
            byte[] prk = hkdf.ComputeHash(chatSalt);
            byte[] info = Encoding.UTF8.GetBytes("FOREST_ROOT_KEY_V1");
            byte[] keyMaterial = hkdf.ComputeHash(prk.Concat(info).ToArray());
            return keyMaterial.Take(32).ToArray();
        }

        private byte[] DeriveChatSalt(string id1, string id2)
        {
            var ordered = new[] { id1, id2 }.OrderBy(id => id).ToArray();
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes($"FOREST_CHAT_SALT|{ordered[0]}|{ordered[1]}")).Take(16).ToArray();
        }

        private string GenerateChatId(string id1, string id2)
        {
            var ordered = new[] { id1, id2 }.OrderBy(id => id).ToArray();
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{ordered[0]}|{ordered[1]}"));
            return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-").Substring(0, 16);
        }

        public void Dispose()
        {
            StopListening();
            foreach (var cts in _handshakeTimers.Values)
                cts?.Cancel();
            _handshakeTimers.Clear();
        }
    }
}