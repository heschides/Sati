using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Proofer.Data;
using Proofer.Models;
using Proofer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using System.Security.RightsManagement;
using System.Text;



namespace Proofer.ViewModels 
{
    public partial class LoginWindowViewModel : ObservableObject
    {

        private readonly IAuthService _authService;
        private readonly IServiceProvider _service;

        public User? LoggedInUser { get; set; }
        public string? Username { get; set; }
        public SecureString? SecurePassword { get; set; }

        public ObservableCollection<User> Users { get; } = new ObservableCollection<User>();

        //constructor
        public LoginWindowViewModel(IAuthService authService, IServiceProvider service)
        {
            _service = service;
            _authService = authService;
            Users.Add(User.Create(0, "Default", "Default", "hashish", "12354"));
        }

        public event EventHandler<bool>? OpenNewUserReq8ested;
        public event EventHandler<bool>? LoginSucceeded;

        [RelayCommand]
        public async Task LoginAsync()
        {
            var userName = Username;
            var password = SecurePassword;
            if (string.IsNullOrWhiteSpace(userName) || password == null)
            {
                return;
            }
            var user = await _authService.AuthenticateAsync(userName, password);
            if (user == null)
                return;

            LoggedInUser = user;
            LoginSucceeded?.Invoke(this, true);
        }

        [RelayCommand]
        public void OpenNewUserWindow()
        {
            OpenNewUserReq8ested?.Invoke(this, true);
        }
    }
}

