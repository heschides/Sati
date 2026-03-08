using Microsoft.EntityFrameworkCore;
using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Security;
using System.Text;
using Windows.UI.Notifications;

namespace Proofer.Data
{
    public class AuthService : IAuthService
    {
        private readonly ProoferContext _context;
        public AuthService(ProoferContext context)
        {
            _context = context;
        }

        public async Task<User?> AuthenticateAsync(string username, SecureString password)
        {
            var userEntity = await _context.Users.SingleOrDefaultAsync(u => u.Username == username);
            if (userEntity == null)
            {
                return null;
            }
                var passwordHasher = new PasswordHasher();
            var isValid = passwordHasher.Verify(
                  password,
                  userEntity.PasswordHash,
                  userEntity.Salt
                 );

                if (!isValid)
                    return null; 
                
                return User.Create(
                    userEntity.Id,
                    userEntity.Username,
                    userEntity.DisplayName,
                    userEntity.PasswordHash,
                    userEntity.Salt
                );

                
            }
        }
    }

