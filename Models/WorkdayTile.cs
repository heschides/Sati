using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Models
{
    public partial class WorkdayTile : ObservableObject
    {
        public DateTime Date { get; init; }
        public string Letter { get; init; } = string.Empty;
        public bool IsInteractable { get; init; }

        [ObservableProperty]
        private bool isExcluded;
    }
}
