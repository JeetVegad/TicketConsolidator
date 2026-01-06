using System;
using System.Security.Cryptography;
using System.Text;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class EncryptionService : IEncryptionService
    {
        // Optional entropy to add extra complexity (should be constant for the app)
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("TicketConsolidator_Salt_2024");

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception)
            {
                // Fallback or rethrow? For security, maybe just return empty or throw?
                // If it fails (e.g. running on non-windows without support), it's critical.
                throw new PlatformNotSupportedException("Encryption failed. Ensure DPAPI is supported on this platform.");
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // If decryption fails (e.g. wrong user, corrupted data, or already plain text?), return null or throw.
                // It's possible the config has plain text (first run). 
                // Let's assume if base64 parsing fails or DPAPI fails, it *might* be plain text? 
                // But confusing plain text with ciphertext is dangerous.
                // Let's assume strict encryption. If it fails, prompts user to re-enter.
                return null; 
            }
        }
    }
}
