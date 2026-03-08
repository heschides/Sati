using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Proofer.Data
{
    public interface IPasswordHasher
    {
         (string Hash, string Salt) HashPassword(SecureString password);
         bool Verify(SecureString password, string storedHash, string storedSalt);
    }
}
