using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public AuthService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<User?> AuthenticateAsync(string username, SecureString password)
        {
            await using var context = _contextFactory.CreateDbContext();

            var userEntity = await context.Users
                .SingleOrDefaultAsync(u => u.Username == username);

            if (userEntity is null || !userEntity.IsEnabled || userEntity.SecurityVersion <= 0)
                return null;

            var passwordHasher = new PasswordHasher();
            var isValid = passwordHasher.Verify(password, userEntity.PasswordHash, userEntity.Salt);

            if (!isValid)
                return null;

            LocalAuditTrail.Record(
                context,
                userEntity,
                LocalAuditActions.AuthenticationSucceeded,
                "User",
                userEntity.Id);
            await context.SaveChangesAsync();

            // Session identity keeps the persisted capabilities, not the legacy role's
            // migration defaults. Password verification material never belongs in it.
            var sessionUser = User.Create(
                userEntity.Id,
                userEntity.Username,
                userEntity.DisplayName,
                string.Empty,
                string.Empty,
                userEntity.Role,
                userEntity.SupervisorId,
                userEntity.AgencyId
            );
            sessionUser.Permissions = userEntity.Permissions;
            sessionUser.IsEnabled = userEntity.IsEnabled;
            sessionUser.SecurityVersion = userEntity.SecurityVersion;
            sessionUser.Email = userEntity.Email;
            sessionUser.Phone = userEntity.Phone;
            return sessionUser;
        }
    }
}
