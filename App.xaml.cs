using System.Configuration;
using System.Data;
using Proofer.Views;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Proofer.Data;
using Proofer.ViewModels;
using Microsoft.Identity.Client;

namespace Proofer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        protected override void OnStartup(StartupEventArgs e)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    //registrations
                    services.AddScoped<IPersonService, PersonService>();
                    services.AddScoped<INoteService, NoteService>();
                    services.AddScoped<IAuthService, AuthService>();
                    services.AddScoped<IUserService, UserService>();
                    services.AddScoped<IPasswordHasher, PasswordHasher>();

                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();

                    services.AddTransient<NewClientViewModel>();
                    services.AddTransient<NewClientWindow>();

                    services.AddTransient<LoginWindow>();
                    services.AddTransient<LoginWindowViewModel>();

                    services.AddTransient<NewUserWindow>();
                    services.AddTransient<NewUserViewModel>();

                    //EF Core
                    services.AddDbContext<ProoferContext>(options => options.UseSqlServer(context.Configuration.GetConnectionString("ProoferDb")));
                })
                .Build();

            _host.Start();


            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.Show();

            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                var user = loginWindow.LoggedInUser;

                if (user == null)
                    return;
                mainVm.Initialize(user);

                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }

            base.OnStartup(e);


        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            base.OnExit(e);
        }
    }

}
