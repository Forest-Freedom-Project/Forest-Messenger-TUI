using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chaos.NaCl;
using Forest.Models;
using ForestMSG.Core.Enums;
using ForestMSG.Core.Logging;
using ForestMSG.Core.Services.FileSystem;

namespace ForestMSG.Core.Services.Encryption
{
    public class EncryptionService
    {
        public class EncryptedMessagePacket
        {
            public string? ChatId { get; set; }
            public string? SenderId { get; set; }
            public ulong MessageId { get; set; }
            public DateTime SentAt { get; set; }
            public MessageType MessageType { get; set; }

            public byte[]? Ciphertext { get; set; }
            public byte[]? Nonce { get; set; }
            public byte[]? AuthTag { get; set; }

            public byte[]? Signature { get; set; }

            public string ToJson() => JsonSerializer.Serialize(this);
            public static EncryptedMessagePacket? FromJson(string json) => JsonSerializer.Deserialize<EncryptedMessagePacket>(json);
        }

        public class EncryptedMediaFile
        {
            public string? OriginalFileName { get; set; }
            public string? MediaType { get; set; }
            public byte[]? Ciphertext { get; set; }
            public byte[]? Nonce { get; set; }
            public byte[]? Tag { get; set; }
        }

        public class ChatSession
        {
            public string? ChatId { get; set; }
            public byte[]? RootKey { get; set; }
            public byte[]? ChatSalt { get; set; }
            public string? SelfId { get; set; }
            public string? PeerId { get; set; }
            public byte[]? PeerPublicKey { get; set; }
            public ulong NextMessageId { get; set; }
        }

        public class MessageEncoder
        {
            private static class ArchiveHelper
            {
                public static async Task<(byte[] ciphertext, byte[] nonce, byte[] tag)> CreateEncryptedArchiveAsync(
                    string sourceFolder,
                    byte[] archiveKey)
                {
                    if (!Directory.Exists(sourceFolder))
                        throw new DirectoryNotFoundException($"Папка не найдена: {sourceFolder}");

                    string tempZip = Path.GetTempFileName() + ".zip";
                    try
                    {
                        ZipFile.CreateFromDirectory(sourceFolder, tempZip);
                        byte[] zipData = await File.ReadAllBytesAsync(tempZip);

                        byte[] nonce = new byte[12];
                        using (var rng = RandomNumberGenerator.Create())
                            rng.GetBytes(nonce);

                        byte[] ciphertext = new byte[zipData.Length];
                        byte[] tag = new byte[16];
                        using (var aes = new AesGcm(archiveKey))
                        {
                            aes.Encrypt(nonce, zipData, ciphertext, tag);
                        }

                        return (ciphertext, nonce, tag);
                    }
                    finally
                    {
                        if (File.Exists(tempZip))
                            File.Delete(tempZip);
                    }
                }

                public static async Task ExtractEncryptedArchiveAsync(
                    byte[] ciphertext,
                    byte[] nonce,
                    byte[] tag,
                    byte[] archiveKey,
                    string destinationFolder)
                {
                    byte[] zipData = new byte[ciphertext.Length];
                    using (var aes = new AesGcm(archiveKey))
                    {
                        aes.Decrypt(nonce, ciphertext, tag, zipData);
                    }

                    string tempZip = Path.GetTempFileName() + ".zip";
                    try
                    {
                        await File.WriteAllBytesAsync(tempZip, zipData);

                        if (Directory.Exists(destinationFolder))
                            Directory.Delete(destinationFolder, true);
                        ZipFile.ExtractToDirectory(tempZip, destinationFolder);
                    }
                    finally
                    {
                        if (File.Exists(tempZip))
                            File.Delete(tempZip);
                    }
                }
            }

            private static byte[] DeriveArchiveKey(byte[] rootKey, byte[] chatSalt, ulong messageId)
            {
                using var hkdf = new HMACSHA256(rootKey);
                byte[] idBytes = BitConverter.GetBytes(messageId);
                byte[] salt = new byte[chatSalt.Length + idBytes.Length];
                Buffer.BlockCopy(chatSalt, 0, salt, 0, chatSalt.Length);
                Buffer.BlockCopy(idBytes, 0, salt, chatSalt.Length, idBytes.Length);

                byte[] prk = hkdf.ComputeHash(salt);
                byte[] info = Encoding.UTF8.GetBytes("FOREST_ARCHIVE_KEY_V1");
                byte[] keyMaterial = hkdf.ComputeHash(prk.Concat(info).ToArray());

                byte[] key = new byte[32];
                Buffer.BlockCopy(keyMaterial, 0, key, 0, 32);
                return key;
            }

            private byte[] DeriveMediaKey(byte[] archiveKey, string mediaType)
            {
                using var hkdf = new HMACSHA256(archiveKey);
                byte[] info = Encoding.UTF8.GetBytes($"FOREST_{mediaType}_KEY_V1");
                byte[] keyMaterial = hkdf.ComputeHash(info);
                byte[] key = new byte[32];
                Buffer.BlockCopy(keyMaterial, 0, key, 0, 32);
                return key;
            }

            private async Task EncryptSingleFileAsync(string filePath, byte[] mediaKey)
            {
                byte[] plaintext = await File.ReadAllBytesAsync(filePath);

                byte[] nonce = new byte[12];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(nonce);

                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[16];

                using (var aes = new AesGcm(mediaKey))
                    aes.Encrypt(nonce, plaintext, ciphertext, tag);

                var encryptedFile = new EncryptedMediaFile
                {
                    OriginalFileName = Path.GetFileName(filePath),
                    Ciphertext = ciphertext,
                    Nonce = nonce,
                    Tag = tag
                };

                string json = JsonSerializer.Serialize(encryptedFile);
                string encryptedFilePath = filePath + ".enc";
                await File.WriteAllTextAsync(encryptedFilePath, json);

                File.Delete(filePath);
            }

            private async Task DecryptSingleFileAsync(string encryptedFilePath, byte[] mediaKey)
            {
                string json = await File.ReadAllTextAsync(encryptedFilePath);
                var encryptedFile = JsonSerializer.Deserialize<EncryptedMediaFile>(json);
                if (encryptedFile == null)
                    throw new CryptographicException("Не удалось прочитать метаданные зашифрованного файла");

                byte[] plaintext = new byte[encryptedFile.Ciphertext.Length];
                using (var aes = new AesGcm(mediaKey))
                {
                    aes.Decrypt(encryptedFile.Nonce, encryptedFile.Ciphertext, encryptedFile.Tag, plaintext);
                }

                string originalFilePath = Path.Combine(
                    Path.GetDirectoryName(encryptedFilePath),
                    encryptedFile.OriginalFileName
                );
                await File.WriteAllBytesAsync(originalFilePath, plaintext);

                File.Delete(encryptedFilePath);
            }

            public async Task<(EncryptedMessagePacket packet, Message message)> EncryptMessageAsync(
                Message message,
                ChatSession session,
                byte[] senderPrivateKey)
            {
                ValidateParameters(message, session, senderPrivateKey);

                if (message.Id == 0)
                {
                    message.Id = session.NextMessageId;
                    session.NextMessageId++;
                }
                if (string.IsNullOrEmpty(message.SenderId))
                    message.SenderId = session.SelfId;
                message.SentAt = DateTime.UtcNow;

                byte[] archiveKey = DeriveArchiveKey(session.RootKey, session.ChatSalt, message.Id);

                string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                try
                {
                    Directory.CreateDirectory(tempFolder);
                    await WriteMessageToFolderAsync(message, tempFolder, archiveKey);

                    var (ciphertext, nonce, tag) = await ArchiveHelper.CreateEncryptedArchiveAsync(tempFolder, archiveKey);

                    byte[] signature = SignMessageData(
                        session.ChatId,
                        message.Id,
                        ciphertext,
                        tag,
                        nonce,
                        senderPrivateKey
                    );

                    var packet = new EncryptedMessagePacket
                    {
                        ChatId = session.ChatId,
                        SenderId = message.SenderId,
                        MessageId = message.Id,
                        SentAt = message.SentAt,
                        MessageType = message.MessageType,
                        Ciphertext = ciphertext,
                        Nonce = nonce,
                        AuthTag = tag,
                        Signature = signature
                    };

                    message.MessageFolderPath = tempFolder;
                    return (packet, message);
                }
                catch
                {
                    if (Directory.Exists(tempFolder))
                        Directory.Delete(tempFolder, true);
                    throw;
                }
            }

            private async Task WriteMessageToFolderAsync(Message message, string folderPath, byte[] archiveKey)
            {
                string infoFolder = Path.Combine(folderPath, "Info");
                Directory.CreateDirectory(infoFolder);

                var meta = new
                {
                    message.Id,
                    message.SenderId,
                    message.ChatId,
                    message.MessageType,
                    message.SentAt,
                    message.IsDownloaded,
                    message.TextFilePath,
                    AudioFiles = message.AudioFiles?.Select(p => Path.GetFileName(p)).ToList(),
                    VoicesFiles = message.VoicesFiles?.Select(p => Path.GetFileName(p)).ToList(),
                    VideoFiles = message.VideoFiles?.Select(p => Path.GetFileName(p)).ToList(),
                    PictureFiles = message.PictureFiles?.Select(p => Path.GetFileName(p)).ToList()
                };
                string metaJson = JsonSerializer.Serialize(meta);
                await File.WriteAllTextAsync(Path.Combine(infoFolder, "message.json"), metaJson);

                byte[] textKey = DeriveMediaKey(archiveKey, "TEXT");
                byte[] audioKey = DeriveMediaKey(archiveKey, "AUDIO");
                byte[] voiceKey = DeriveMediaKey(archiveKey, "VOICE");
                byte[] videoKey = DeriveMediaKey(archiveKey, "VIDEO");
                byte[] imageKey = DeriveMediaKey(archiveKey, "IMAGE");

                if (!string.IsNullOrEmpty(message.TextFilePath))
                {
                    string textFolder = Path.Combine(folderPath, "Text");
                    Directory.CreateDirectory(textFolder);
                    string textFilePath = Path.Combine(textFolder, "content.txt");

                    if (File.Exists(message.TextFilePath))
                    {
                        string textContent = await File.ReadAllTextAsync(message.TextFilePath);
                        await File.WriteAllTextAsync(textFilePath, textContent);
                    }

                    await EncryptSingleFileAsync(textFilePath, textKey);
                }

                await EncryptMediaFilesAsync(message.AudioFiles, folderPath, "Audio", audioKey);
                await EncryptMediaFilesAsync(message.VoicesFiles, folderPath, "Voices", voiceKey);
                await EncryptMediaFilesAsync(message.VideoFiles, folderPath, "Videos", videoKey);
                await EncryptMediaFilesAsync(message.PictureFiles, folderPath, "Pictures", imageKey);
            }

            private async Task EncryptMediaFilesAsync(List<string> filePaths, string folderPath, string subfolder, byte[] mediaKey)
            {
                if (filePaths == null || !filePaths.Any()) return;

                string targetFolder = Path.Combine(folderPath, subfolder);
                Directory.CreateDirectory(targetFolder);

                foreach (string filePath in filePaths)
                {
                    if (!File.Exists(filePath)) continue;
                    string destFile = Path.Combine(targetFolder, Path.GetFileName(filePath));
                    File.Copy(filePath, destFile, true);
                    await EncryptSingleFileAsync(destFile, mediaKey);
                }
            }

            public async Task<Message> DecryptMessageAsync(
                EncryptedMessagePacket packet,
                ChatSession session)
            {
                ValidatePacket(packet);

                if (!VerifyMessageSignature(packet, session.PeerPublicKey))
                {
                    throw new CryptographicException($"Неверная подпись сообщения {packet.MessageId}");
                }

                byte[] archiveKey = DeriveArchiveKey(session.RootKey, session.ChatSalt, packet.MessageId);

                string extractFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                try
                {
                    await ArchiveHelper.ExtractEncryptedArchiveAsync(
                        packet.Ciphertext,
                        packet.Nonce,
                        packet.AuthTag,
                        archiveKey,
                        extractFolder
                    );

                    string metaPath = Path.Combine(extractFolder, "Info", "message.json");
                    if (!File.Exists(metaPath))
                        throw new FileNotFoundException("Отсутствует файл метаданных в архиве");

                    string metaJson = await File.ReadAllTextAsync(metaPath);
                    var meta = JsonSerializer.Deserialize<MessageMetadata>(metaJson);
                    if (meta == null)
                        throw new CryptographicException("Не удалось десериализовать метаданные");

                    var message = new Message
                    {
                        Id = meta.Id,
                        SenderId = meta.SenderId,
                        ChatId = meta.ChatId,
                        MessageType = meta.MessageType,
                        SentAt = meta.SentAt,
                        IsDownloaded = true,
                        MessageFolderPath = extractFolder,
                        TextFilePath = meta.TextFilePath
                    };

                    if (!string.IsNullOrEmpty(meta.TextFilePath))
                    {
                        string textEncPath = Path.Combine(extractFolder, meta.TextFilePath + ".enc");
                        if (File.Exists(textEncPath))
                        {
                            byte[] textKey = DeriveMediaKey(archiveKey, "TEXT");
                            await DecryptSingleFileAsync(textEncPath, textKey);
                        }
                    }

                    message.AudioFiles = await DecryptMediaFilesAsync(extractFolder, "Audio", DeriveMediaKey(archiveKey, "AUDIO"));
                    message.VoicesFiles = await DecryptMediaFilesAsync(extractFolder, "Voices", DeriveMediaKey(archiveKey, "VOICE"));
                    message.VideoFiles = await DecryptMediaFilesAsync(extractFolder, "Videos", DeriveMediaKey(archiveKey, "VIDEO"));
                    message.PictureFiles = await DecryptMediaFilesAsync(extractFolder, "Pictures", DeriveMediaKey(archiveKey, "IMAGE"));

                    if (message.Id >= session.NextMessageId)
                        session.NextMessageId = message.Id + 1;

                    return message;
                }
                catch
                {
                    if (Directory.Exists(extractFolder))
                        Directory.Delete(extractFolder, true);
                    throw;
                }
            }

            private async Task<List<string>> DecryptMediaFilesAsync(string baseFolder, string subfolder, byte[] mediaKey)
            {
                string folder = Path.Combine(baseFolder, subfolder);
                if (!Directory.Exists(folder))
                    return new List<string>();

                foreach (string encryptedFile in Directory.GetFiles(folder, "*.enc"))
                {
                    await DecryptSingleFileAsync(encryptedFile, mediaKey);
                }

                return Directory.GetFiles(folder)
                    .Where(f => !f.EndsWith(".enc"))
                    .ToList();
            }

            private class MessageMetadata
            {
                public ulong Id { get; set; }
                public string? SenderId { get; set; }
                public string? ChatId { get; set; }
                public MessageType MessageType { get; set; }
                public DateTime SentAt { get; set; }
                public bool IsDownloaded { get; set; }
                public string? TextFilePath { get; set; }
                public List<string>? AudioFiles { get; set; }
                public List<string>? VoicesFiles { get; set; }
                public List<string>? VideoFiles { get; set; }
                public List<string>? PictureFiles { get; set; }
            }

            private void ValidateParameters(Message message, ChatSession session, byte[] senderPrivateKey)
            {
                if (message == null) throw new ArgumentNullException(nameof(message));
                if (session == null) throw new ArgumentNullException(nameof(session));
                if (senderPrivateKey == null || senderPrivateKey.Length != 64)
                    throw new ArgumentException("PrivateKey must be 64 bytes");

                bool hasText = !string.IsNullOrEmpty(message.TextFilePath) && File.Exists(message.TextFilePath);
                bool hasMedia = (message.AudioFiles != null && message.AudioFiles.Any()) ||
                                (message.VoicesFiles != null && message.VoicesFiles.Any()) ||
                                (message.VideoFiles != null && message.VideoFiles.Any()) ||
                                (message.PictureFiles != null && message.PictureFiles.Any());
                if (!hasText && !hasMedia)
                    throw new ArgumentException("Сообщение не содержит ни текста, ни медиа");
            }

            private void ValidatePacket(EncryptedMessagePacket packet)
            {
                if (packet == null) throw new ArgumentNullException(nameof(packet));
                if (string.IsNullOrEmpty(packet.ChatId)) throw new ArgumentException("ChatId is null");
                if (string.IsNullOrEmpty(packet.SenderId)) throw new ArgumentException("SenderId is null");
                if (packet.Ciphertext == null || packet.Ciphertext.Length == 0) throw new ArgumentException("Ciphertext is empty");
                if (packet.Nonce == null || packet.Nonce.Length != 12) throw new ArgumentException("Nonce must be 12 bytes");
                if (packet.AuthTag == null || packet.AuthTag.Length != 16) throw new ArgumentException("AuthTag must be 16 bytes");
                if (packet.Signature == null || packet.Signature.Length == 0) throw new ArgumentException("Signature is empty");
            }

            private byte[] SignMessageData(string chatId, ulong messageId, byte[] ciphertext, byte[] authTag, byte[] nonce, byte[] senderPrivateKey)
            {
                byte[] chatIdBytes = Encoding.UTF8.GetBytes(chatId);
                byte[] idBytes = BitConverter.GetBytes(messageId);
                byte[] dataToSign = new byte[chatIdBytes.Length + idBytes.Length + ciphertext.Length + authTag.Length + nonce.Length];
                int offset = 0;
                Buffer.BlockCopy(chatIdBytes, 0, dataToSign, offset, chatIdBytes.Length);
                offset += chatIdBytes.Length;
                Buffer.BlockCopy(idBytes, 0, dataToSign, offset, idBytes.Length);
                offset += idBytes.Length;
                Buffer.BlockCopy(ciphertext, 0, dataToSign, offset, ciphertext.Length);
                offset += ciphertext.Length;
                Buffer.BlockCopy(authTag, 0, dataToSign, offset, authTag.Length);
                offset += authTag.Length;
                Buffer.BlockCopy(nonce, 0, dataToSign, offset, nonce.Length);
                return Ed25519.Sign(dataToSign, senderPrivateKey);
            }

            private bool VerifyMessageSignature(EncryptedMessagePacket packet, byte[] senderPublicKey)
            {
                try
                {
                    byte[] chatIdBytes = Encoding.UTF8.GetBytes(packet.ChatId);
                    byte[] idBytes = BitConverter.GetBytes(packet.MessageId);
                    byte[] dataToVerify = new byte[chatIdBytes.Length + idBytes.Length + packet.Ciphertext.Length + packet.AuthTag.Length + packet.Nonce.Length];
                    int offset = 0;
                    Buffer.BlockCopy(chatIdBytes, 0, dataToVerify, offset, chatIdBytes.Length);
                    offset += chatIdBytes.Length;
                    Buffer.BlockCopy(idBytes, 0, dataToVerify, offset, idBytes.Length);
                    offset += idBytes.Length;
                    Buffer.BlockCopy(packet.Ciphertext, 0, dataToVerify, offset, packet.Ciphertext.Length);
                    offset += packet.Ciphertext.Length;
                    Buffer.BlockCopy(packet.AuthTag, 0, dataToVerify, offset, packet.AuthTag.Length);
                    offset += packet.AuthTag.Length;
                    Buffer.BlockCopy(packet.Nonce, 0, dataToVerify, offset, packet.Nonce.Length);
                    return Ed25519.Verify(packet.Signature, dataToVerify, senderPublicKey);
                }
                catch { return false; }
            }

            public string SaveEncryptedMessageToFile(EncryptedMessagePacket packet, string folderPath)
            {
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);
                string fileName = $"{packet.ChatId}_{packet.SenderId}_{packet.MessageId}.enc";
                string filePath = Path.Combine(folderPath, fileName);
                File.WriteAllText(filePath, packet.ToJson());
                return filePath;
            }

            public async Task<Message> LoadAndDecryptMessageFromFileAsync(string filePath, ChatSession session)
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
                string encryptedJson = await File.ReadAllTextAsync(filePath);
                var packet = EncryptedMessagePacket.FromJson(encryptedJson);
                return await DecryptMessageAsync(packet, session);
            }
        }

        public class CryptoKeysGenerator
        {
            public class KeyPair
            {
                public byte[] PublicKey { get; set; } = new byte[32];
                public byte[] PrivateKey { get; set; } = new byte[64];

                public byte[] EncryptionPrivateKey { get; set; } = new byte[32];
                public byte[] EncryptionPublicKey { get; set; } = new byte[32];

                public string MnemonicPhrase { get; set; }
                public DateTime GenerateAt { get; set; } = DateTime.UtcNow;

                public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);
                public string EncryptionPublicKeyBase64 => Convert.ToBase64String(EncryptionPublicKey);
            }

            public static KeyPair GenerateNewKeyPair()
            {
                byte[] ed25519Seed = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(ed25519Seed);
                }

                Ed25519.KeyPairFromSeed(out byte[] ed25519PublicKey, out byte[] ed25519PrivateKey, ed25519Seed);

                byte[] x25519PrivateKey = new byte[32];
                byte[] x25519PublicKey = new byte[32];

                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(x25519PrivateKey);
                }

                x25519PrivateKey = Ed25519.ExpandedPrivateKeyFromSeed(ed25519Seed).Take(32).ToArray();
                x25519PublicKey = MontgomeryCurve25519.GetPublicKey(x25519PrivateKey);

                return new KeyPair
                {
                    PublicKey = ed25519PublicKey,
                    PrivateKey = ed25519PrivateKey,
                    EncryptionPrivateKey = x25519PrivateKey,
                    EncryptionPublicKey = x25519PublicKey,
                    MnemonicPhrase = null
                };
            }

            public static KeyPair GenerateFromMnemonic(string mnemonicPhrase, string passphrase = "")
            {
                byte[] masterSeed = DeriveSeedFromMnemonic(mnemonicPhrase, passphrase);

                byte[] ed25519Seed = masterSeed.Take(32).ToArray();

                Ed25519.KeyPairFromSeed(out byte[] ed25519PublicKey, out byte[] ed25519PrivateKey, ed25519Seed);

                byte[] x25519PrivateKey = Ed25519.ExpandedPrivateKeyFromSeed(ed25519Seed).Take(32).ToArray();
                byte[] x25519PublicKey = MontgomeryCurve25519.GetPublicKey(x25519PrivateKey);

                return new KeyPair
                {
                    PublicKey = ed25519PublicKey,
                    PrivateKey = ed25519PrivateKey,
                    EncryptionPrivateKey = x25519PrivateKey,
                    EncryptionPublicKey = x25519PublicKey,
                    MnemonicPhrase = mnemonicPhrase
                };
            }

            private static byte[] DeriveSeedFromMnemonic(string mnemonicPhrase, string passphrase)
            {
                string normalizedPhrase = mnemonicPhrase.Trim().ToLowerInvariant().Replace("  ", " ");

                string salt = $"FOREST_MNEMONIC_SALT|{passphrase}";

                var pbkdf2 = new Rfc2898DeriveBytes(
                    normalizedPhrase,
                    Encoding.UTF8.GetBytes(salt),
                    2048
                );

                return pbkdf2.GetBytes(64);
            }

            public static byte[] SignData(byte[] data, KeyPair keyPair)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                if (keyPair?.PrivateKey == null) throw new ArgumentNullException(nameof(keyPair));

                return Ed25519.Sign(data, keyPair.PrivateKey);
            }
            public static byte[] SignData(byte[] data, byte[] privateKey)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));
                if (privateKey == null || privateKey.Length != 64)
                    throw new ArgumentException("PrivateKey must be 64 bytes");
                return Ed25519.Sign(data, privateKey);
            }

            public static bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
            {
                if (data == null || signature == null || publicKey == null) { return false; }

                try
                {
                    return Ed25519.Verify(signature, data, publicKey);
                }
                catch
                {
                    return false;
                }
            }

            public static byte[] ComputeSharedSecret(byte[] myPrivateKey, byte[] peerPublicKey)
            {
                if (myPrivateKey.Length != 32 || peerPublicKey.Length != 32)
                { throw new ArgumentException("Keys must have 32 bytes"); }

                try
                {
                    return MontgomeryCurve25519.KeyExchange(peerPublicKey, myPrivateKey);
                }
                catch
                {
                    throw new CryptographicException("ECDH Error");
                }
            }

            public static string ExportKeyPair(KeyPair keyPair, string encryptionPassword)
            {
                var exportData = new
                {
                    Version = "1.0",
                    PublicKey = Convert.ToBase64String(keyPair.PublicKey),
                    EncryptedPrivateKey = EncryptPrivateKey(keyPair.PrivateKey, encryptionPassword),

                    EncryptionPublicKey = Convert.ToBase64String(keyPair.EncryptionPublicKey),
                    GeneratedAt = keyPair.GenerateAt.ToString("o")
                };

                return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            private static string EncryptPrivateKey(byte[] privateKey, string password)
            {
                byte[] salt = new byte[16];
                var rng = RandomNumberGenerator.Create();
                rng.GetBytes(salt);

                var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000);
                byte[] key = pbkdf2.GetBytes(32);

                using var aes = new AesGcm(key);
                byte[] nonce = new byte[12];
                rng.GetBytes(nonce);
                byte[] ciphertext = new byte[privateKey.Length];
                byte[] tag = new byte[16];

                aes.Encrypt(nonce, privateKey, ciphertext, tag);

                byte[] result = new byte[salt.Length + nonce.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
                Buffer.BlockCopy(nonce, 0, result, salt.Length, nonce.Length);
                Buffer.BlockCopy(ciphertext, 0, result, salt.Length + nonce.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, salt.Length + nonce.Length + ciphertext.Length, tag.Length);

                return Convert.ToBase64String(result);
            }
            public static KeyPair ImportKeyPair(string encryptedData, string password)
            {
                var exportData = JsonSerializer.Deserialize<ExportData>(encryptedData);

                byte[] privateKey = DecryptPrivateKey(exportData.EncryptedPrivateKey, password);

                byte[] publicKey = Convert.FromBase64String(exportData.PublicKey);
                byte[] encryptionPublicKey = Convert.FromBase64String(exportData.EncryptionPublicKey);

                byte[] encryptionPrivateKey = Ed25519.ExpandedPrivateKeyFromSeed(privateKey.Take(32).ToArray())
                    .Take(32).ToArray();

                return new KeyPair
                {
                    PublicKey = publicKey,
                    PrivateKey = privateKey,
                    EncryptionPrivateKey = encryptionPrivateKey,
                    EncryptionPublicKey = encryptionPublicKey,
                    GenerateAt = exportData.GeneratedAt
                };
            }

            private class ExportData
            {
                public string? Version { get; set; }
                public string? PublicKey { get; set; }
                public string? EncryptedPrivateKey { get; set; }
                public string? EncryptionPublicKey { get; set; }
                public DateTime GeneratedAt { get; set; }
            }

            private static byte[] DecryptPrivateKey(string encryptedPrivateKeyBase64, string password)
            {
                byte[] encryptedData = Convert.FromBase64String(encryptedPrivateKeyBase64);

                byte[] salt = new byte[16];
                Buffer.BlockCopy(encryptedData, 0, salt, 0, 16);

                byte[] nonce = new byte[12];
                Buffer.BlockCopy(encryptedData, 16, nonce, 0, 12);

                int tagLength = 16;
                int ciphertextLength = encryptedData.Length - 16 - 12 - 16;
                byte[] ciphertext = new byte[ciphertextLength];
                byte[] tag = new byte[tagLength];
                Buffer.BlockCopy(encryptedData, 16 + 12, ciphertext, 0, ciphertextLength);
                Buffer.BlockCopy(encryptedData, 16 + 12 + ciphertextLength, tag, 0, tagLength);

                using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000);
                byte[] key = pbkdf2.GetBytes(32);

                byte[] plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);

                return plaintext;
            }
        }

        public class PhrasesGenerator
        {
            public static string WebPath = "https://people.sc.fsu.edu/~jburkardt/datasets/words/anagram_dictionary.txt";
            public static string LocalDictionaryPath = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Security, DirectoryNames.BaseDictionary);
            public static string LocalBaseMnemonicPhrasePath = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Security, DirectoryNames.Mnemonic);
            public static short WordsCount = 24;

            public static void CreateMnemonicDictionary()
            {
                try
                {
                    if (!Directory.Exists(LocalBaseMnemonicPhrasePath))
                    {
                        Directory.CreateDirectory(LocalBaseMnemonicPhrasePath);
                    }
                    if (!File.Exists(LocalDictionaryPath))
                    {
                        var webClient = new WebClient();
                        var dictionary = webClient.DownloadString(WebPath);
                        File.WriteAllText(LocalDictionaryPath, dictionary);
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteLog($"[Encryption Service] {e}");
                }
            }

            public static void WriteSecureMnemonicPhraseString(string folderName, string fileName)
            {
                var phrase = CreateSecureMnemonicPhraseString().Split(' ');
                var fullPath = Path.Combine(LocalBaseMnemonicPhrasePath, folderName, fileName);

                if (!Directory.Exists(Path.Combine(LocalBaseMnemonicPhrasePath, folderName)))
                {
                    Directory.CreateDirectory(Path.Combine(LocalBaseMnemonicPhrasePath, folderName));
                }
                if (!File.Exists(fullPath))
                {
                    File.WriteAllText(fullPath, JsonSerializer.Serialize(phrase));
                }
            }

            public static string CreateSecureMnemonicPhraseString()
            {
                try
                {
                    var dictionary = File.ReadAllLines(LocalDictionaryPath);
                    var words = new List<string>(WordsCount);

                    using (var rng = RandomNumberGenerator.Create())
                    {
                        byte[] randomBuffer = new byte[WordsCount * 4];
                        rng.GetBytes(randomBuffer);

                        for (int i = 0; i < WordsCount; i++)
                        {
                            uint randomNumber = BitConverter.ToUInt32(randomBuffer, i * 4);
                            int index = (int)(randomNumber % (uint)dictionary.Length);
                            words.Add(dictionary[index]);
                        }
                    }

                    return string.Join(" ", words);
                }
                catch (Exception e)
                {
                    Logger.WriteLog($"[Encryption Service] {e}");
                    return null;
                }
            }

            public static string CreateChecksum(string phrase)
            {
                var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(phrase));

                string checksum = BitConverter.ToString(hash, 0, 2)
                    .Replace("-", "")
                    .ToLower();

                return $"{checksum}";
            }

            public static bool CheckUpCheckSum(string phrase, string checksum)
            {
                if (CreateChecksum(phrase) == checksum)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}