using Sati.Models;
using System.Security;

namespace Sati.Data
{
    public interface IUserService
    {
        Task<User> CreateAsync(User user);
        Task<List<User>> GetAllAsync();
        Task UpdateAsync(User user);
        Task ResetPasswordAsync(User user, string newPassword);
    }
}