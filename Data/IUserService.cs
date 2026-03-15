using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IUserService
    {
        Task <List<User>> GetAllAsync();
        Task<User> CreateAsync(User user);
    }
}
