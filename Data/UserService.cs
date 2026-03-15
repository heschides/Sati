using Microsoft.EntityFrameworkCore;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public partial class UserService : IUserService
    {
        private readonly SatiContext _context;
        public async Task<User> CreateAsync(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<List<User>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public UserService(SatiContext context)
        {
            _context = context;
        }
    }
}
