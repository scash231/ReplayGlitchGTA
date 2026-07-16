using System;
using System.Text;

namespace WinNetSyncTool
{
    internal static class StringCipher
    {
        // Custom key. Should be relatively random.
        private static readonly byte[] Key = new byte[] { 0x7B, 0x1A, 0xC4, 0x89, 0x3F, 0xE2 };

        public static string Decrypt(byte[] encryptedBytes)
        {
            byte[] decrypted = new byte[encryptedBytes.Length];
            for (int i = 0; i < encryptedBytes.Length; i++)
            {
                decrypted[i] = (byte)(encryptedBytes[i] ^ Key[i % Key.Length]);
            }
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
