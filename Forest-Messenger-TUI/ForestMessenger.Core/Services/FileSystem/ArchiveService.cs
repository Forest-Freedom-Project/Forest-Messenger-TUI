using System.IO.Compression;
using System.Security.Cryptography;

namespace ForestMSG.Core.Services.FileSystem
{
    public class ArchiveService
    {
        public async Task<byte[]> CreateEncryptedArchiveAsync(string folderPath, byte[] archiveKey)
        {
            if (!Directory.Exists(folderPath))
            { throw new DirectoryNotFoundException($"Папка не найдена: {folderPath}"); }

            if (archiveKey == null || archiveKey.Length != 32)
            { throw new ArgumentException("Ключ шифрования должен быть 32 байта", nameof(archiveKey)); }

            string tempZip = Path.GetTempFileName() + ".zip";
            try
            {
                ZipFile.CreateFromDirectory(folderPath, tempZip);

                byte[] zipData = await File.ReadAllBytesAsync(tempZip);

                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                byte[] ciphertext = new byte[zipData.Length];

                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonce);
                }

                using (var aes = new AesGcm(archiveKey))
                {
                    aes.Encrypt(nonce, zipData, ciphertext, tag);
                }

                byte[] result = new byte[nonce.Length + tag.Length + ciphertext.Length];

                Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
                Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
                Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

                return result;
            }
            finally
            {
                if (File.Exists(tempZip))
                { File.Delete(tempZip); }
            }
        }


        public async Task ExtractEncryptedArchiveAsync(byte[] encryptedData, byte[] archiveKey, string extractPath)
        {
            if (encryptedData == null || encryptedData.Length < 12 + 16)
            { throw new ArgumentException("Зашифрованные данные повреждены", nameof(encryptedData)); }

            if (archiveKey == null || archiveKey.Length != 32)
            { throw new ArgumentException("Ключ шифрования должен быть 32 байта", nameof(archiveKey)); }

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[encryptedData.Length - 12 - 16];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, 12);
            Buffer.BlockCopy(encryptedData, 12, tag, 0, 16);
            Buffer.BlockCopy(encryptedData, 12 + 16, ciphertext, 0, ciphertext.Length);

            byte[] zipData = new byte[ciphertext.Length];
            using (var aes = new AesGcm(archiveKey))
            {
                aes.Decrypt(nonce, ciphertext, tag, zipData);
            }

            string tempZip = Path.GetTempFileName() + ".zip";
            try
            {
                await File.WriteAllBytesAsync(tempZip, zipData);

                if (Directory.Exists(extractPath))
                { Directory.Delete(extractPath, true); }

                ZipFile.ExtractToDirectory(tempZip, extractPath);
            }
            finally
            {
                if (File.Exists(tempZip))
                { File.Delete(tempZip); }
            }
        }

        public bool IsEncryptedArchive(byte[] data)
        {
            return data != null && data.Length >= 12 + 16;
        }

        public async Task<byte[]> CreatePlainArchiveAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {folderPath}");

            string tempZip = Path.GetTempFileName() + ".zip";
            try
            {
                ZipFile.CreateFromDirectory(folderPath, tempZip);
                return await File.ReadAllBytesAsync(tempZip);
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }
    }
}