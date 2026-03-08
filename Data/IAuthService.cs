using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace Proofer.Data
{
    public interface IAuthService
    {
        Task<User?> AuthenticateAsync(string username, SecureString password);
    }
}
