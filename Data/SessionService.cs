using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

using Sati.Services;

namespace Sati.Data
{
    public class SessionService : ISessionService, ISessionLifetime
    {
        private readonly object _gate = new();
        public User? CurrentUser { get; private set; }
        public bool HasSessionEnded { get; private set; }
        public event EventHandler? SessionEnded;
        public bool AllowComplianceOverride { get; set; } = false;


        public void SetUser(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            var captured = User.Create(user.Id, user.Username, user.DisplayName, string.Empty, string.Empty,
                user.Role, user.SupervisorId, user.AgencyId);
            captured.Permissions = user.Permissions;
            captured.Email = user.Email;
            captured.Phone = user.Phone;
            captured.IsEnabled = user.IsEnabled;
            captured.SecurityVersion = user.SecurityVersion;
            lock (_gate)
            {
                CurrentUser = captured;
                HasSessionEnded = false;
                AllowComplianceOverride = false;
            }
        }

        public void Invalidate(User capturedUser)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(CurrentUser, capturedUser) || HasSessionEnded)
                    return;
                HasSessionEnded = true;
                AllowComplianceOverride = false;
            }
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Invalidate()
        {
            if (CurrentUser is User captured)
                Invalidate(captured);
        }
    }
}

