using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public partial class UserService : IUserService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public UserService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<User> CreateAsync(User user)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        public async Task<List<User>> GetAllAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users.ToListAsync();
        }
    }
}