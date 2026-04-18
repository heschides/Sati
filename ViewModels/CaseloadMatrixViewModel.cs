using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels
{
    public class CaseloadMatrixViewModel
    {
        public ObservableCollection<Person> People { get; }

        public CaseloadMatrixViewModel(ObservableCollection<Person> people)
        {
            People = people;
        }
    }
}