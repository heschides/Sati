using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
        public interface ISettingsService
        {
            Task<Settings> LoadAsync();
            Task SaveAsync(Settings settings);
        }
}
