using Proofer.Data;
using Proofer.Models;
using Proofer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;


namespace Proofer.Views
{

    public partial class LoginWindow : Window
    {
        public LoginWindow(LoginWindowViewModel vm)
        {
            var services = ((App)Application.Current).Services;
            DataContext = vm;

            InitializeComponent();

            vm.OpenNewUserReq8ested += (s, success) =>
            {
                var services = ((App)Application.Current).Services;
                var win = new NewUserWindow(services.GetRequiredService<NewUserViewModel>()
                    );
                var result = win.ShowDialog();
                
                if(result == true && win.CreatedUser is User newUser)
                {
                    vm.Users.Add(newUser);
                    vm.LoggedInUser = newUser;
                }
            };
        }
        public User? LoggedInUser =>
            (DataContext as LoginWindowViewModel)?.LoggedInUser;
    }
}
