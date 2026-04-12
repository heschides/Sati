using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using System.Windows;

namespace Sati
{
    public partial class App : Application
    {
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Services
                    services.AddTransient<IPersonService, PersonService>();
                    services.AddTransient<INoteService, NoteService>();
                    services.AddTransient<IAuthService, AuthService>();
                    services.AddTransient<IUserService, UserService>();
                    services.AddTransient<IScratchpadService, ScratchpadService>();
                    services.AddTransient<IPasswordHasher, PasswordHasher>();
                    services.AddTransient<IIncentiveService, IncentiveService>();
                    services.AddSingleton<ISessionService, SessionService>();
                    services.AddTransient<ISettingsService, SettingsService>();
                    services.AddTransient<IUpcomingEventService, UpcomingEventService>();
                    services.AddTransient<IFormService, FormService>();

                    // Shell
                    services.AddSingleton<ShellViewModel>();
                    services.AddSingleton<ShellWindow>();

                    // Child ViewModels
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<ScratchpadViewModel>();

                    // Modal windows and their ViewModels
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<LoginWindowViewModel>();

                    services.AddTransient<NewUserWindow>();
                    services.AddTransient<NewUserViewModel>();

                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<SettingsWindow>();

                    services.AddTransient<NotesWindowViewModel>();
                    services.AddTransient<NotesWindow>();

                    services.AddTransient<ComplianceReviewViewModel>();
                    services.AddTransient<ComplianceReviewWindow>();

                    services.AddTransient<ScratchpadHistoryViewModel>();
                    services.AddTransient<ScratchpadHistoryWindow>();

                    services.AddSingleton<GuidanceViewModel>();
                    services.AddSingleton<HelpersViewModel>();

                    services.AddTransient<SchedulerViewModel>();
                    services.AddTransient<NewClientWindow>();

                    services.AddSingleton<SupervisorDashboardViewModel>();

                    // Factories
                    services.AddTransient<Func<string, UserMessageDialog>>(sp => message => new UserMessageDialog(message));
                    services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                    services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                    services.AddTransient<Func<NewClientWindow>>(sp => () => sp.GetRequiredService<NewClientWindow>());
                    services.AddTransient<Func<ScratchpadHistoryWindow>>(sp => () => sp.GetRequiredService<ScratchpadHistoryWindow>());
                    services.AddTransient<Func<NotesWindow>>(sp => () => sp.GetRequiredService<NotesWindow>());

                    // EF Core
                    services.AddDbContextFactory<SatiContext>(options =>
                        options.UseSqlServer(context.Configuration.GetConnectionString("SatiDb")),
                        ServiceLifetime.Transient);
                })
                .Build();

            _host.Start();

            // Migrate database
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SatiContext>();
            db.Database.Migrate();

            // Login sequence
            var splash = new SplashScreenWindow();
            splash.Show();
            await Task.Delay(3000);
            splash.Close();

            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                var user = loginWindow.LoggedInUser;
                if (user == null) { Shutdown(); return; }

                var session = _host.Services.GetRequiredService<ISessionService>();
                session.SetUser(user);

                var shellVm = _host.Services.GetRequiredService<ShellViewModel>();
                await shellVm.InitializeAsync();

                var shellWindow = _host.Services.GetRequiredService<ShellWindow>();
                shellWindow.Show();
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