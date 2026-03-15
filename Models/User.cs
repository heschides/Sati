using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Windows.Media.Audio;

namespace Sati.Models
{
    public class User
    {
        public int Id { get; private set; } 
        public string Username { get; private set; } = string.Empty;
        public string DisplayName { get; private set; } = string.Empty;
        public string PasswordHash { get; private set; } = string.Empty;
        public string Salt { get; private set; } = string.Empty;



        private User() { }
        private User(int id, string username, string displayName, string passwordHash, string salt)
        {
            Id = id;
            Username = username;
            DisplayName = displayName;
            PasswordHash = passwordHash;
            Salt = salt;
        }

        public static User Create(int id, string username, string displayName, string passwordHash, string salt)
        {
            return new User(id, username, displayName, passwordHash, salt);
        }
    }
}
