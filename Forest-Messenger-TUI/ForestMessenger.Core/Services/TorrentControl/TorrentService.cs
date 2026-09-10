using System.Text;
using System.Text.Json;
using ForestMSG.Core.Logging;
using ForestMSG.Core.Models;
using ForestMSG.Core.Services.FileSystem;
using MonoTorrent;
using MonoTorrent.Client;

namespace ForestMSG.Core.Services.TorrentControl
{
    public class TorrentService
    {
        public class ContactTorrentService : TorrentService
        {
            public async Task StartContactTorrentAsync(string contactFolderPath, string contactFileName)
            {
                var settings = new EngineSettingsBuilder
                {
                    AutoSaveLoadDhtCache = true,
                    AllowLocalPeerDiscovery = true,
                }.ToSettings();

                var engine = new ClientEngine(settings);

                var contactTorrent = await Task.Run(() =>
                    Torrent.Load(Path.Combine(contactFolderPath, contactFileName)));

                var manager = await engine.AddAsync(contactTorrent, contactFolderPath);

                await manager.StartAsync();

                string magnetLink = manager.MagnetLink?.ToV1String() ?? "N/A";

                var logMessage = $"Torrent: {contactTorrent.Name}\n"
                + $"State: {manager.State}\n"
                + $"CanUseDht: {manager.CanUseDht}\n"
                + $"Complete: {manager.Complete}\n"
                + $"Peers Available: {manager.Peers.Available}\n"
                + $"Magnet: {magnetLink}";
                Logger.WriteLog(logMessage);

                manager.TorrentStateChanged += (s, e) =>
                    Logger.WriteLog($"State changed to: {manager.State}");
                manager.PeerConnected += (s, e) =>
                    Logger.WriteLog($"Peer connected: {e.Peer.Uri}");
            }
            public async Task CreateContactTorrentAsync(string contactJsonPath, string contactId, bool isPrivate = false)
            {
                if (!File.Exists(contactJsonPath))
                {
                    throw new FileNotFoundException($"Файл контакта не найден: {contactJsonPath}");
                }

                string contactTorrentFolder = Path.Combine(_contactsTorrentFolder, contactId);
                Directory.CreateDirectory(contactTorrentFolder);

                string torrentPath = Path.Combine(contactTorrentFolder, $"{contactId}.torrent");

                var creator = new TorrentCreator
                {
                    Comment = $"Forest Contact: {contactId}",
                    CreatedBy = "Forest Messenger v1.0",
                    Name = $"forest_contact_{contactId}"
                };

                if (!isPrivate)
                {
                    creator.Announces.Add(PublicTrackers);
                }

                await Task.Run(() => creator.Create(new TorrentFileSource(contactJsonPath), torrentPath));
            }
            public async Task<Contact> FindContactInDHTAsync(string publicId)
            {
                string contactFolder = Path.Combine(_contactsFolder, publicId);
                string jsonPath = Path.Combine(contactFolder, $"{publicId}.json");

                if (File.Exists(jsonPath))
                {
                    string json = await File.ReadAllTextAsync(jsonPath);
                    return JsonSerializer.Deserialize<Contact>(json);
                }

                try
                {
                    string infoHashHex = GenerateInfoHashFromPublicId(publicId);
                    var infoHash = InfoHash.FromHex(infoHashHex);

                    var magnetLink = new MagnetLink(infoHash, publicId, PublicTrackers);

                    var manager = await engine.AddAsync(magnetLink, _torrentsFolder);
                    await manager.StartAsync();

                    if (manager.Torrent == null)
                    {
                        Logger.WriteLog($"[TorrentService] Торрент не содержит метаданных для {publicId}");
                        return null;
                    }

                    string downloadPath = Path.Combine(_torrentsFolder, "Downloads");
                    Directory.CreateDirectory(downloadPath);

                    var jsonFile = manager.Torrent.Files.FirstOrDefault(f =>
                f.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

                    if (jsonFile == null)
                    {
                        Logger.WriteLog($"[TorrentService] JSON файл не найден в торренте {publicId}");
                        return null;
                    }

                    while (manager.Bitfield.PercentComplete < 100 && manager.State != TorrentState.Seeding)
                    {
                        await Task.Delay(1000);
                    }

                    string downloadedFilePath = Path.Combine(downloadPath, publicId, jsonFile.Path);
                    if (!File.Exists(downloadedFilePath))
                    {
                        Logger.WriteLog($"[TorrentService] Файл не скачан: {downloadedFilePath}");
                        return null;
                    }

                    string json = await File.ReadAllTextAsync(downloadedFilePath);
                    var contact = JsonSerializer.Deserialize<Contact>(json);

                    await manager.StopAsync();

                    if (contact != null && contact.PublicId == publicId)
                    {
                        Directory.CreateDirectory(contactFolder);
                        await File.WriteAllTextAsync(jsonPath, json);

                        Logger.WriteLog($"[TorrentService] Контакт {publicId} найден и сохранён локально");
                        return contact;
                    }

                    return null;

                }
                catch (Exception e)
                {
                    Logger.WriteLog($"[TorrentService] {e}");
                    return null;
                }
            }
            public async Task PublishContactAsync(Contact contact)
            {
                if (contact == null)
                    throw new ArgumentNullException(nameof(contact));

                string contactFolder = Path.Combine(_contactsFolder, contact.PublicId);
                Directory.CreateDirectory(contactFolder);

                string jsonPath = Path.Combine(contactFolder, $"{contact.PublicId}.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(contact, options);
                await File.WriteAllTextAsync(jsonPath, json);

                await CreateContactTorrentAsync(jsonPath, contact.PublicId, contact.IsPrivate);

                string torrentPath = Path.Combine(_contactsTorrentFolder, contact.PublicId, $"{contact.PublicId}.torrent");
                var torrent = await Task.Run(() => Torrent.Load(torrentPath));

                var manager = await engine.AddAsync(torrent, _contactsFolder);
                await manager.StartAsync();

                Logger.WriteLog($"[TorrentService] Контакт {contact.PublicId} опубликован");
                Logger.WriteLog($"  State: {manager.State}");
                Logger.WriteLog($"  Magnet: {manager.MagnetLink?.ToV1String() ?? "N/A"}");

                manager.TorrentStateChanged += (s, e) =>
                    Logger.WriteLog($"State changed to: {manager.State}");
                manager.PeerConnected += (s, e) =>
                    Logger.WriteLog($"Peer connected: {e.Peer.Uri}");
            }
            private string GenerateInfoHashFromPublicId(string publicId)
            {
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(publicId));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            public async Task PublishHandshakeAsync(HandshakePacket handshake)
            {
                if (handshake == null)
                { throw new ArgumentNullException(nameof(handshake)); }

                string handshakeFolder = Path.Combine(_torrentsFolder, "Handshakes");
                Directory.CreateDirectory(handshakeFolder);

                string jsonPath = Path.Combine(handshakeFolder, $"{handshake.ChatId}_handshake.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(handshake, options);
                await File.WriteAllTextAsync(jsonPath, json);

                string torrentName = $"{handshake.ChatId}_handshake.torrent";
                string torrentPath = Path.Combine(handshakeFolder, torrentName);

                var creator = new TorrentCreator
                {
                    Comment = $"Forest Handshake: {handshake.ChatId}",
                    CreatedBy = "Forest Messenger v1.0",
                    Name = $"handshake_{handshake.ChatId}",
                    Private = true
                };

                creator.Announces.Add(PublicTrackers);

                await Task.Run(() => creator.Create(new TorrentFileSource(jsonPath), torrentPath));

                var torrent = await Task.Run(() => Torrent.Load(torrentPath));
                var manager = await engine.AddAsync(torrent, handshakeFolder);
                await manager.StartAsync();

                Logger.WriteLog($"[TorrentService] Рукопожатие {handshake.ChatId} опубликовано");
                Logger.WriteLog($"State: {manager.State}");
                Logger.WriteLog($"Magnet: {manager.MagnetLink?.ToV1String() ?? "N/A"}");

                manager.TorrentStateChanged += (s, e) =>
                    Logger.WriteLog($"Handshake state changed to: {manager.State}");
                manager.PeerConnected += (s, e) =>
                    Logger.WriteLog($"Handshake peer connected: {e.Peer.Uri}");
            }
            public async Task PublishConfirmationAsync(HandshakeConfirmation confirmation)
            {
                if (confirmation == null)
                { throw new ArgumentException(nameof(confirmation)); }

                string handshakeFolder = Path.Combine(_torrentsFolder, "Handshakes");
                Directory.CreateDirectory(handshakeFolder);

                string jsonPath = Path.Combine(handshakeFolder, $"{confirmation.ChatId}_confirmation.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(confirmation, options);
                await File.WriteAllTextAsync(jsonPath, json);

                string torrentName = $"{confirmation.ChatId}_confirmation.torrent";
                string torrentPath = Path.Combine(handshakeFolder, torrentName);

                var creator = new TorrentCreator
                {
                    Comment = $"Forest Confirmation: {confirmation.ChatId}",
                    CreatedBy = "Forest Messenger v1.0",
                    Name = $"confirmation_{confirmation.ChatId}",
                    Private = false
                };

                creator.Announces.Add(PublicTrackers);

                var torrent = await Task.Run(() => Torrent.Load(torrentPath));
                var manager = await engine.AddAsync(torrent, handshakeFolder);
                await manager.StartAsync();

                Logger.WriteLog($"[TorrentService] Квитанция для {confirmation.ChatId} опубликовано");
                Logger.WriteLog($"State: {manager.State}");
            }

            public async Task<List<HandshakePacket>> FindHandshakesForMeAsync(string myPublicId)
            {
                var result = new List<HandshakePacket>();
                string handshakeFolder = Path.Combine(_torrentsFolder, "Handshakes");

                if (!Directory.Exists(handshakeFolder))
                { return result; }

                try
                {
                    var torrentFiles = Directory.GetFiles(handshakeFolder, "*_handshake.torrent");

                    foreach (var torrentPath in torrentFiles)
                    {
                        try
                        {
                            var torrent = await Task.Run(() => Torrent.Load(torrentPath));

                            var jsonFile = torrent.Files.FirstOrDefault(
                                f => f.Path.EndsWith("_handshake.json", StringComparison.OrdinalIgnoreCase)
                            );

                            if (jsonFile == null)
                            { continue; }

                            string jsonPath = Path.Combine(handshakeFolder, jsonFile.Path);
                            if (!File.Exists(jsonPath))
                            { continue; }

                            string json = await File.ReadAllTextAsync(jsonPath);
                            var handshake = JsonSerializer.Deserialize<HandshakePacket>(json);

                            if (handshake == null)
                            { continue; }

                            if (handshake.RecipientId == myPublicId)
                            {
                                if (!handshake.IsExpired())
                                {
                                    result.Add(handshake);
                                }
                                else
                                {
                                    File.Delete(torrentPath);
                                    File.Delete(jsonPath);
                                    Logger.WriteLog($"[TorrentService] Удалено просроченное рукопожатие {handshake.ChatId}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLog($"[TorrentService] Ошибка обработки {torrentPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[TorrentService] Ошибка поиска рукопожатий: {ex.Message}");
                }

                return result;
            }
            public async Task RemoveHandshakeFromDHTAsync(string chatId)
            {
                string handshakeFolder = Path.Combine(_torrentsFolder, "Handshakes");
                string torrentPath = Path.Combine(handshakeFolder, $"{chatId}_handshake.torrent");
                string jsonPath = Path.Combine(handshakeFolder, $"{chatId}_handshake.json");

                try
                {
                    foreach (var manager in engine.Torrents)
                    {
                        if (manager.Torrent?.Name == $"handshake_{chatId}")
                        {
                            await manager.StopAsync();
                            await engine.RemoveAsync(manager);
                            Logger.WriteLog($"[TorrentService] раздача рукопожатия {chatId} остановлена");
                            break;
                        }
                    }

                    if (File.Exists(torrentPath))
                    {
                        File.Delete(torrentPath);
                        Logger.WriteLog($"[TorrentService] Удален .torrent: {torrentPath}");
                    }

                    if (File.Exists(jsonPath))
                    {
                        File.Delete(jsonPath);
                        Logger.WriteLog($"[TorrentService] Удален JSON: {jsonPath}");
                    }

                    string confTorrentPath = Path.Combine(handshakeFolder, $"{chatId}_confirmation.torrent");
                    string confJsonPath = Path.Combine(handshakeFolder, $"{chatId}_confirmation");

                    if (File.Exists(confTorrentPath))
                    {
                        foreach (var manager in engine.Torrents)
                        {
                            if (manager.Torrent?.Name == $"confirmation_{chatId}")
                            {
                                await manager.StopAsync();
                                await engine.RemoveAsync(manager);
                                break;
                            }
                        }
                        File.Delete(confTorrentPath);
                        File.Delete(confJsonPath);
                        Logger.WriteLog($"[TorrentService] Удалена квитанция для {chatId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[TorrentService] Ошибка удаления рукопожатия {chatId}: {ex.Message}");
                }
            }
        }
        public class MessageTorrentService : TorrentService
        {
            public async Task PublishMessageAsync(string encryptedFilePath, string chatId)
            {
                if (!File.Exists(encryptedFilePath))
                    throw new FileNotFoundException($"Файл сообщения не найден: {encryptedFilePath}");

                string torrentPath = await CreateTorrentFromFileAsync(encryptedFilePath, chatId, "msg");

                var torrent = await Task.Run(() => Torrent.Load(torrentPath));
                var manager = await engine.AddAsync(torrent, Path.GetDirectoryName(encryptedFilePath));
                await manager.StartAsync();

                Logger.WriteLog($"[TorrentService] Сообщение для чата {chatId} опубликовано");
                Logger.WriteLog($"  State: {manager.State}");
            }

            private async Task<string> CreateTorrentFromFileAsync(string filePath, string id, string type = "file")
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Файл не найден: {filePath}");

                string torrentFolder = Path.Combine(_torrentsFolder, "Messages");
                Directory.CreateDirectory(torrentFolder);

                string torrentPath = Path.Combine(torrentFolder, $"{id}_{type}_{Path.GetFileName(filePath)}.torrent");

                var creator = new TorrentCreator
                {
                    Comment = $"Forest {type}: {id}",
                    CreatedBy = "Forest Messenger v1.0",
                    Name = $"{type}_{id}",
                    Private = false
                };

                creator.Announces.Add(PublicTrackers);

                await Task.Run(() => creator.Create(new TorrentFileSource(filePath), torrentPath));

                Logger.WriteLog($"[TorrentService] .torrent создан: {torrentPath}");
                return torrentPath;
            }
        }
        private readonly ClientEngine engine;
        private readonly string _torrentsFolder;

        private readonly string _contactsTorrentFolder;
        private readonly string _contactsFolder;
        private readonly List<string> PublicTrackers = new List<string>()
        {
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://tracker.coppersurfer.tk:6969/announce",
            "udp://tracker.leechers-paradise.org:6969/announce",
            "udp://tracker.cyberia.is:6969/announce"
        };

        public TorrentService(string baseFolder = null)
        {
            string mainFolder = baseFolder ?? DirectoryNames.MainFolder;

            _contactsFolder = Path.Combine(mainFolder, DirectoryNames.Contacts);
            _torrentsFolder = Path.Combine(mainFolder, DirectoryNames.Torrents);
            _contactsTorrentFolder = Path.Combine(_torrentsFolder, DirectoryNames.TorrentContacts);

            Directory.CreateDirectory(_contactsFolder);
            Directory.CreateDirectory(_torrentsFolder);
            Directory.CreateDirectory(_contactsTorrentFolder);

            var engineSettings = new EngineSettingsBuilder()
            {
                AllowPortForwarding = true,
                AutoSaveLoadDhtCache = true,
                AllowLocalPeerDiscovery = true
            }.ToSettings();

            engine = new ClientEngine(engineSettings);

            engine.StartAllAsync();
        }
    }
}
