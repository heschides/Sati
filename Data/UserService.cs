using Microsoft.EntityFrameworkCore;
using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    public partial class UserService : IUserService
    {
        private readonly ProoferContext _context;
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

        public UserService(ProoferContext context)
        {
            _context = context;
        }
    }
}
