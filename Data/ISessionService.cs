using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface ISessionService
    {
        User? CurrentUser { get; }
        void SetUser(User user);
    }
}
