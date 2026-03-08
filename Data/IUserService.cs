using Proofer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    internal interface IUserService
    {
        Task <List<User>> GetAllAsync();
        Task<User> CreateAsync(User user);
    }
}
