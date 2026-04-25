using Sati.Models;


namespace Sati.Data
{
    public interface IFormService
    {
        Task UpdateFormAsync(Form form);
        Task OpenFormAsync(Form form);
    }
}
