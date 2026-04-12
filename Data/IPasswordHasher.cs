using System.Security;

namespace Sati.Data
{
    public interface IPasswordHasher
    {
        (string Hash, string Salt) HashPassword(SecureString password);
        (string Hash, string Salt) HashPassword(string password);
        bool Verify(SecureString password, string storedHash, string storedSalt);
    }
}