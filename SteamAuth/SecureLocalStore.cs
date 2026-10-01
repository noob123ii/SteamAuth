using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SyntaX
{
    internal static class SecureLocalStore
    {
        private static readonly byte[] MagicDpapi = Encoding.ASCII.GetBytes("SXENC1");
        private static readonly byte[] MagicAes = Encoding.ASCII.GetBytes("SXENC2");

        public static bool WriteText(string path, string content)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (!AuthService.EnsureLocalFolder())
                return false;

            byte[] plain = Encoding.UTF8.GetBytes(content ?? "");

            if (TryWriteProtected(path, plain, MagicDpapi, ProtectDpapi, UnprotectDpapi))
                return true;

            if (TryWriteProtected(path, plain, MagicAes, ProtectAes, UnprotectAes))
                return true;

            return TryWriteRaw(path, plain);
        }

        public static string ReadText(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (StartsWith(raw, MagicDpapi))
                {
                    string text = ReadProtected(raw, MagicDpapi.Length, UnprotectDpapi);
                    if (text != null)
                        return text;
                    return ReadProtected(raw, MagicDpapi.Length, UnprotectDpapiLegacy);
                }
                if (StartsWith(raw, MagicAes))
                    return ReadProtected(raw, MagicAes.Length, UnprotectAes);

                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                return null;
            }
        }

        public static string ReadTextAndUpgrade(string path)
        {
            string text = ReadText(path);
            if (text == null)
                return null;

            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (!StartsWith(raw, MagicDpapi) && !StartsWith(raw, MagicAes))
                    WriteText(path, text);
            }
            catch { }

            return text;
        }

        public static void Delete(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch { }
        }

        private static bool TryWriteProtected(
            string path,
            byte[] plain,
            byte[] magic,
            Func<byte[], byte[]> protect,
            Func<byte[], byte[]> unprotect)
        {
            try
            {
                byte[] payload = protect(plain);
                if (payload == null || payload.Length == 0)
                    return false;

                var data = new byte[magic.Length + payload.Length];
                Buffer.BlockCopy(magic, 0, data, 0, magic.Length);
                Buffer.BlockCopy(payload, 0, data, magic.Length, payload.Length);
                return WriteBytesAtomic(path, data);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryWriteRaw(string path, byte[] plain)
        {
            try
            {
                return WriteBytesAtomic(path, plain);
            }
            catch
            {
                return false;
            }
        }

        private static string ReadProtected(byte[] raw, int payloadOffset, Func<byte[], byte[]> unprotect)
        {
            if (raw.Length <= payloadOffset)
                return null;

            byte[] payload = new byte[raw.Length - payloadOffset];
            Buffer.BlockCopy(raw, payloadOffset, payload, 0, payload.Length);
            byte[] plain = unprotect(payload);
            return plain == null ? null : Encoding.UTF8.GetString(plain);
        }

        private static bool WriteBytesAtomic(string path, byte[] data)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                return false;

            Directory.CreateDirectory(directory);
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.WriteAllBytes(tempPath, data);
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                try
                {
                    File.SetAttributes(path, FileAttributes.Hidden);
                }
                catch { }

                return true;
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private static byte[] ProtectDpapi(byte[] plain)
        {
            try
            {
                return ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] UnprotectDpapi(byte[] payload)
        {
            try
            {
                return ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ProtectAes(byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = GetAesKey();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                    var combined = new byte[aes.IV.Length + cipher.Length];
                    Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
                    Buffer.BlockCopy(cipher, 0, combined, aes.IV.Length, cipher.Length);
                    return combined;
                }
            }
        }

        private static byte[] UnprotectAes(byte[] payload)
        {
            if (payload == null || payload.Length <= 16)
                return null;

            using (var aes = Aes.Create())
            {
                aes.Key = GetAesKey();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                byte[] iv = new byte[16];
                byte[] cipher = new byte[payload.Length - 16];
                Buffer.BlockCopy(payload, 0, iv, 0, 16);
                Buffer.BlockCopy(payload, 16, cipher, 0, cipher.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                    return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        private static byte[] UnprotectDpapiLegacy(byte[] payload)
        {
            try
            {
                return ProtectedData.Unprotect(payload, GetLegacyEntropy(), DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] GetLegacyEntropy()
        {
            const string salt = "GorillaTagUnbanner|sx-local-v1";
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(salt));
        }

        private static byte[] GetAesKey()
        {
            string seed = Environment.UserName + "|" + Environment.MachineName + "|GorillaTagUnbanner|sx-aes-v1";
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        }

        private static bool StartsWith(byte[] raw, byte[] magic)
            => raw != null && magic != null && raw.Length > magic.Length && raw.Take(magic.Length).SequenceEqual(magic);
    }
}
