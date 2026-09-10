using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ForestMSG.Core.Models;
using ForestMSG.Core.Services.Encryption;
using ForestMSG.Core.Services.FileSystem;
using ForestMSG.Core.Services.TorrentControl;
using static ForestMSG.Core.Services.Encryption.EncryptionService;

namespace ForestMSG.Core.Services.ContactManagement
{
    public class ContactService
    {
        private readonly string _contactsFolder;
        private readonly TorrentService.ContactTorrentService? _torrentService;

        public ContactService(TorrentService.ContactTorrentService? torrentService = null)
        {
            _contactsFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Contacts);
            _torrentService = torrentService;

            Directory.CreateDirectory(_contactsFolder);
        }

        public async Task<(Contact contact, string mnemonic)> CreateUserAsync(string name, string password, bool isPrivate = true)
        {
            string mnemonic = PhrasesGenerator.CreateSecureMnemonicPhraseString();
            var keyPair = CryptoKeysGenerator.GenerateFromMnemonic(mnemonic);

            string salt = IdGenerator.GenerateSalt(32);

            string publicId = IdGenerator.GeneratePublicUserId(name, salt);

            var contact = new Contact(
                name: name,
                publicId: publicId,
                publicKey: keyPair.PublicKeyBase64,
                encryptionKey: keyPair.EncryptionPublicKeyBase64,
                isPrivate: isPrivate
            );

            contact.Salt = salt;

            SignContact(contact, keyPair.PrivateKey);

            await SaveContactAsync(contact);

            await SaveContactAsMeAsync(contact);

            await SaveKeysAsync(keyPair, contact.PublicId, password);
            await SaveMnemonicAsync(mnemonic, contact.PublicId);

            return (contact, mnemonic);

        }

        private async Task SaveContactAsMeAsync(Contact contact)
        {
            string meFolder = Path.Combine(_contactsFolder, DirectoryNames.Me);
            Directory.CreateDirectory(meFolder);

            string mePath = Path.Combine(meFolder, "Me.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(contact, options);
            await File.WriteAllTextAsync(mePath, json);

        }

        public async Task<CryptoKeysGenerator.KeyPair> LoadKeysAsync(string publicId, string password = "")
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Пароль не может быть пустым");

            string keysPath = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Security, publicId, "keys.encrypted");
            if (!File.Exists(keysPath))
                throw new FileNotFoundException($"Файл ключей не найден: {keysPath}");

            string encryptedData = await File.ReadAllTextAsync(keysPath);
            return CryptoKeysGenerator.ImportKeyPair(encryptedData, password);

        }

        private async Task SaveMnemonicAsync(string mnemonic, string publicId)
        {
            string securityFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Security, publicId);
            Directory.CreateDirectory(securityFolder);

            string mnemonicPath = Path.Combine(securityFolder, "mnemonic.txt");
            await File.WriteAllTextAsync(mnemonicPath, mnemonic);
        }

        private async Task SaveKeysAsync(CryptoKeysGenerator.KeyPair keyPair, string publicId, string password)
        {
            if (string.IsNullOrEmpty(password))
            { throw new ArgumentException("Пароль не может быть пустым"); }

            string securityFolder = Path.Combine(DirectoryNames.MainFolder, DirectoryNames.Security, publicId);
            Directory.CreateDirectory(securityFolder);

            string encryptedKeys = CryptoKeysGenerator.ExportKeyPair(keyPair, password);
            string keysPath = Path.Combine(securityFolder, "keys.encrypted");
            await File.WriteAllTextAsync(keysPath, encryptedKeys);
        }

        public async Task<Contact?> LoadContactAsync(string publicId)
        {
            if (string.IsNullOrEmpty(publicId))
            { throw new ArgumentException("PublicId не может быть пустым"); }

            string filePath = Path.Combine(_contactsFolder, publicId, $"{publicId}.json");

            if (!File.Exists(filePath))
            { return null; }

            string json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<Contact>(json);
        }

        public async Task<Contact?> LoadMyContactAsync()
        {
            string mePath = Path.Combine(_contactsFolder, "Me", "Me.json");
            if (!File.Exists(mePath))
                return null;

            string json = await File.ReadAllTextAsync(mePath);
            return JsonSerializer.Deserialize<Contact>(json);
        }

        public async Task<Contact?> FindContactInDHTAsync(string publicId)
        {
            if (_torrentService == null)
            { throw new InvalidOperationException("TorrenService не инициализирован"); }

            var localContact = await LoadContactAsync(publicId);
            if (localContact != null && VerifyContact(localContact))
            { return localContact; }

            var contact = await _torrentService.FindContactInDHTAsync(publicId);

            if (contact != null && VerifyContact(contact))
            {
                await SaveContactAsync(contact);
                return contact;
            }

            return null;
        }

        public async Task PublishContactAsync(Contact contact)
        {
            if (_torrentService == null)
            { throw new InvalidOperationException("TorrentService не инициализирован"); }

            if (!VerifyContact(contact))
            { throw new InvalidOperationException("Контакт не подписан или подпись недействительная"); }

            await SaveContactAsync(contact);

            await _torrentService.PublishContactAsync(contact); ;
        }

        private async Task SaveContactAsync(Contact contact)
        {
            if (contact == null || string.IsNullOrEmpty(contact.PublicId))
            { throw new ArgumentException("Контакт или PublicId не может быть null"); }

            string contactFolder = Path.Combine(_contactsFolder, contact.PublicId);
            Directory.CreateDirectory(contactFolder);

            string filePath = Path.Combine(contactFolder, $"{contact.PublicId}.json");

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(contact, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public void SignContact(Contact contact, byte[] privateKey)
        {
            var dataToSign = new
            {
                contact.PublicId,
                contact.Name,
                contact.Avatar,
                contact.Bio,
                contact.PublicKey,
                contact.EncryptionKey,
                contact.IsPrivate,
                contact.CreatedAt,
                contact.Version
            };

            string json = JsonSerializer.Serialize(dataToSign);
            byte[] data = Encoding.UTF8.GetBytes(json);

            byte[] signature = CryptoKeysGenerator.SignData(data, privateKey);
            contact.Signature = Convert.ToBase64String(signature);
        }

        public bool VerifyContact(Contact contact)
        {
            if (string.IsNullOrEmpty(contact.Signature))
            {
                return false;
            }

            var dataToVerify = new
            {
                contact.PublicId,
                contact.Name,
                contact.Avatar,
                contact.Bio,
                contact.PublicKey,
                contact.EncryptionKey,
                contact.IsPrivate,
                contact.CreatedAt,
                contact.Version
            };

            string json = JsonSerializer.Serialize(dataToVerify);
            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] signature = Convert.FromBase64String(contact.Signature);
            byte[] publicKey = Convert.FromBase64String(contact.PublicKey);

            return CryptoKeysGenerator.VerifySignature(data, signature, publicKey);
        }

    }
}