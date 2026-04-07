using Sati.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for NotesWindow.xaml
    /// </summary>
    public partial class NotesWindow : Window
    {
        public NotesWindow(NotesWindowViewModel vm, MainWindowViewModel mainVm)
        {
            DataContext = vm;
            InitializeComponent();

            EventHandler handler = async (s, e) => await vm.ReloadAsync();
            mainVm.NoteChanged += handler;

            Closing += (s, e) => mainVm.NoteChanged -= handler;
        }
    }
}
