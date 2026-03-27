using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IFormService
    {
        Task UpdateFormAsync(Form form);
    }
}
