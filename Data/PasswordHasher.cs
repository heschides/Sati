using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Sati.Data
{
    public class PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16; // 128-bit
        private const int KeySize = 32;  // 256-bit
        private const int Iterations = 100_000;

        public (string Hash, string Salt) HashPassword(SecureString password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Hash(password, salt);

            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        public bool Verify(SecureString password, string storedHash, string storedSalt)
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var hashBytes = Convert.FromBase64String(storedHash);

            var computedHash = Hash(password, saltBytes);

            return CryptographicOperations.FixedTimeEquals(computedHash, hashBytes);
        }

        private static byte[] Hash(SecureString password, byte[] salt)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(ToUnsecureString(password));

            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );
        }


        private static string ToUnsecureString(SecureString secure)
        {
            var unmanaged = IntPtr.Zero;
            try
            {
                unmanaged = Marshal.SecureStringToGlobalAllocUnicode(secure);
                return Marshal.PtrToStringUni(unmanaged)!;
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanaged);
            }
        }
    }
}
