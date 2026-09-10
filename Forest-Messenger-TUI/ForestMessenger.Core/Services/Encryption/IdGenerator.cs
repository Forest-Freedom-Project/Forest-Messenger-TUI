using System.Security.Cryptography;
using System.Text;

namespace ForestMSG.Core.Services.Encryption
{
    public class IdGenerator
    {
        public static string GeneratePublicUserId(string seed, string salt)
        {
            if (string.IsNullOrEmpty(seed))
                throw new ArgumentException("Seed не может быть пустым");
            if (string.IsNullOrEmpty(salt))
                throw new ArgumentException("Соль не может быть пустой");

            string raw = seed + "|" + salt;
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static bool VerifyId(string id, string seed, string salt)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(seed) || string.IsNullOrEmpty(salt))
                return false;

            string expectedId = GeneratePublicUserId(seed, salt);
            return string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase);
        }

        public static string GenerateSalt(int length = 32)
        {
            byte[] saltBytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }
    }
}