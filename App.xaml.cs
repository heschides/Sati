using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.Models;
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
                    services.AddSingleton<GuidanceViewModel>();
                    services.AddSingleton<HelpersViewModel>();
                    services.AddSingleton<SupervisorDashboardViewModel>();
                    services.AddTransient<UserManagementViewModel>();

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
                    services.AddTransient<SwitchUserViewModel>();
                    services.AddTransient<SwitchUserWindow>();
                    services.AddTransient<SchedulerViewModel>();
                    services.AddTransient<NewClientWindow>();
                    services.AddTransient<NewClientViewModel>();

                    // Factories
                    services.AddTransient<Func<string, UserMessageDialog>>(sp => message => new UserMessageDialog(message));
                    services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                    services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                    services.AddTransient<Func<NewClientWindow>>(sp => () => sp.GetRequiredService<NewClientWindow>());
                    services.AddTransient<Func<ScratchpadHistoryWindow>>(sp => () => sp.GetRequiredService<ScratchpadHistoryWindow>());
                    services.AddTransient<Func<NotesWindow>>(sp => () => sp.GetRequiredService<NotesWindow>());
                    services.AddTransient<Func<SwitchUserWindow>>(sp => () => sp.GetRequiredService<SwitchUserWindow>());
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
            await SeedMockDataAsync(_host.Services);

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

        private static async Task SeedMockDataAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SatiContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            // One-time guard — if Wolverine exists, we've already seeded
            if (await context.Users.AnyAsync(u => u.Username == "wolverine"))
                return;

            var random = new Random(42);
            var today = DateTime.Today;

            // -------------------------------------------------------------------------
            // Get Shantideva's ID for supervisor assignment
            // -------------------------------------------------------------------------
            var shantideva = await context.Users
                .FirstOrDefaultAsync(u => u.Username == "shantideva");
            int? supervisorId = shantideva?.Id;

            // -------------------------------------------------------------------------
            // Show rosters — 10 shows, 15 clients each
            // -------------------------------------------------------------------------
            var shows = new Dictionary<string, string[]>
            {
                ["House MD"] = new[]
                {
            "Gregory House", "James Wilson", "Lisa Cuddy", "Allison Cameron",
            "Robert Chase", "Eric Foreman", "Lawrence Kutner", "Chris Taub",
            "Remy Hadley", "Martha Masters", "Jessica Adams", "Chi Park",
            "Amber Volakis", "Edward Vogler", "Stacy Warner"
        },
                ["Cheers"] = new[]
                {
            "Sam Malone", "Diane Chambers", "Carla Tortelli", "Cliff Clavin",
            "Norm Peterson", "Ernie Pantusso", "Frasier Crane", "Lilith Sternin",
            "Rebecca Howe", "Woody Boyd", "Paul Krapence", "Phil Howser",
            "Al Rosen", "Linda Antonelli", "Henri Devereaux"
        },
                ["Night Court"] = new[]
                {
            "Harry Stone", "Dan Fielding", "Bull Shannon", "Mel Torme",
            "Roz Russell", "Christine Sullivan", "Mac Robinson", "Quon Le Duc",
            "Selma Hacker", "Buddy Ryan", "Tony Giuliano", "Marsha Donahue",
            "Bob Wheeler", "Lisette Hocheiser", "Yakov Korolenko"
        },
                ["Who's the Boss"] = new[]
                {
            "Tony Micelli", "Angela Bower", "Mona Robinson", "Jonathan Bower",
            "Samantha Micelli", "Billy Napier", "Kathleen Haskell", "Bonnie",
            "Jesse Riegert", "Andy Crenshaw", "Roger Carey", "Hank Bower",
            "Shirley Micelli", "Chuck", "Becky"
        },
                ["The Cosby Show"] = new[]
                {
            "Cliff Huxtable", "Clair Huxtable", "Denise Huxtable", "Theodore Huxtable",
            "Vanessa Huxtable", "Rudy Huxtable", "Sondra Huxtable", "Elvin Tibideaux",
            "Cockroach Williams", "Kenny Randolph", "Martin Kendall", "Pam Tucker",
            "Olivia Kendall", "Charmaine Brown", "Peter Chiara"
        },
                ["Slings & Arrows"] = new[]
                {
            "Geoffrey Tennant", "Ellen Fanshaw", "Richard Smith-Jones", "Darren Nichols",
            "Oliver Welles", "Anna Conroy", "Frank McKay", "Cyril Sinclair",
            "Kate McNab", "Maria Gillard", "Jerry Langdon", "Jack Crew",
            "Holly Day", "Henry Breedlove", "Charles Kingman"
        },
                ["Barry"] = new[]
                {
            "Barry Berkman", "Monroe Fuches", "Sally Reed", "NoHo Hank",
            "Gene Cousineau", "Cristobal Sifuentes", "Natalie Greer", "Albert Nguyen",
            "Leo Cousineau", "Fernando Groach", "Loach Moss", "Goran Milovic",
            "Esther Velencoso", "Taylor Garrett", "Jermaine Stewart"
        },
                ["The Drew Carey Show"] = new[]
                {
            "Drew Carey", "Mimi Bobeck", "Kate O'Brien", "Oswald Harvey",
            "Lewis Kiniski", "Mr. Wick", "Kellie Newmark", "Nicki Fifer",
            "Lisa Robbins", "Larry Almada", "Chuck Mitchell", "Winfred Ottman",
            "Steve Carey", "Carey Lewis", "Don Malone"
        },
                ["Scrubs"] = new[]
                {
            "John Dorian", "Christopher Turk", "Elliot Reid", "Carla Espinosa",
            "Perry Cox", "Glenn Matthews", "Jordan Sullivan", "Bob Kelso",
            "Laverne Roberts", "Todd Quinlan", "Doug Murphy", "Ted Buckland",
            "Kim Briggs", "Molly Clock", "Lloyd Braddock"
        },
                ["Family Ties"] = new[]
                {
            "Alex Keaton", "Mallory Keaton", "Jennifer Keaton", "Andy Keaton",
            "Skippy Handelman", "Nick Moore", "Lauren Miller", "Ellen Reed",
            "Steven Keaton", "Elyse Keaton", "Andrew Keaton", "Irwin Handelman",
            "Shelley Keaton", "Michael Gross", "Suzanne Valentine"
        }
            };

            // -------------------------------------------------------------------------
            // Case managers — superhero aliases, one per show
            // -------------------------------------------------------------------------
            var caseManagers = new[]
            {
        ("wolverine",     "Wolverine",      "House MD"),
        ("storm",         "Storm",          "Cheers"),
        ("daredevil",     "Daredevil",      "Night Court"),
        ("blackwidow",    "Black Widow",    "Who's the Boss"),
        ("lukecage",      "Luke Cage",      "The Cosby Show"),
        ("ironfist",      "Iron Fist",      "Slings & Arrows"),
        ("nightwing",     "Nightwing",      "Barry"),
        ("jessicajones",  "Jessica Jones",  "The Drew Carey Show"),
        ("hawkeye",       "Hawkeye",        "Scrubs"),
        ("spiderman",     "Spider-Man",     "Family Ties"),
    };

            var noteStatuses = new[]
            {
        NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged,
        NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged,
        NoteStatus.Pending, NoteStatus.Pending, NoteStatus.Pending, NoteStatus.Pending,
        NoteStatus.Abandoned, NoteStatus.Abandoned, NoteStatus.Abandoned,
        NoteStatus.Scheduled, NoteStatus.Scheduled, NoteStatus.Scheduled,
        NoteStatus.Cancelled,
        NoteStatus.Delayed
    };

            var noteTypes = new[]
            {
        NoteType.Visit, NoteType.Visit, NoteType.Visit, NoteType.Visit,
        NoteType.Visit, NoteType.Visit, NoteType.Visit,
        NoteType.Contact, NoteType.Contact, NoteType.Contact,
        NoteType.Other
    };

            var narratives = new[]
            {
        "Conducted home visit. Consumer was cooperative and engaged.",
        "Completed monthly check-in. No concerns noted at this time.",
        "Reviewed service plan. Consumer expressed satisfaction with current supports.",
        "Attempted contact. Left voicemail, no response.",
        "Met with consumer and support agency to discuss service coordination.",
        "Consumer reported difficulty with transportation. Discussed alternatives.",
        "Completed documentation review. All forms current.",
        "Consumer attended day program. Staff reported positive engagement.",
        "Discussed upcoming 90-day review with consumer and guardian.",
        "Follow-up on previous contact. Issue resolved satisfactorily.",
        "Consumer requested change in service schedule. Documented and forwarded.",
        "Coordinated with medical provider regarding consumer health needs.",
        "Attended ISP meeting. Goals reviewed and updated.",
        "Consumer reported feeling well. No changes to service plan needed.",
        "Conducted compliance review. All documentation in order.",
    };

            // -------------------------------------------------------------------------
            // Create users and their clients
            // -------------------------------------------------------------------------
            var (defaultHash, defaultSalt) = hasher.HashPassword("defaultpassword");

            foreach (var (username, displayName, show) in caseManagers)
            {
                // Create user
                var user = User.Create(
                    id: 0,
                    username: username,
                    displayName: displayName,
                    passwordHash: defaultHash,
                    salt: defaultSalt,
                    role: UserRole.CaseManager,
                    supervisorId: supervisorId);

                context.Users.Add(user);
                await context.SaveChangesAsync();

                // Create clients from the assigned show
                var clients = shows[show];
                foreach (var clientName in clients)
                {
                    var nameParts = clientName.Split(' ', 2);
                    var firstName = nameParts[0];
                    var lastName = nameParts.Length > 1 ? nameParts[1] : "Unknown";

                    // Random effective date between 1 and 4 years ago
                    var daysAgo = random.Next(365, 365 * 4);
                    var effectiveDate = today.AddDays(-daysAgo);

                    // Random birthdate between 25 and 65 years ago
                    var birthDaysAgo = random.Next(365 * 25, 365 * 65);
                    var birthDate = today.AddDays(-birthDaysAgo);

                    var person = Person.CreatePerson(
                        userId: user.Id,
                        firstName: firstName,
                        lastName: lastName,
                        bio: $"Consumer enrolled in {show} waiver program.",
                        birthdate: birthDate,
                        effective: effectiveDate,
                        waiver: random.Next(2) == 0 ? WaiverType.Section21 : WaiverType.Section29);

                    context.People.Add(person);
                    await context.SaveChangesAsync();

                    // Create 20 notes per client
                    for (int i = 0; i < 20; i++)
                    {
                        var status = noteStatuses[random.Next(noteStatuses.Length)];
                        var noteType = noteTypes[random.Next(noteTypes.Length)];
                        var narrative = narratives[random.Next(narratives.Length)];

                        // Random event date within the last 90 days
                        var eventDate = today.AddDays(-random.Next(0, 90));

                        var note = Note.Create(
                            narrative: narrative,
                            eventDate: eventDate,
                            status: status,
                            unitCount: random.Next(1, 8),
                            personId: person.Id,
                            noteType: noteType);

                        context.Notes.Add(note);
                    }

                    await context.SaveChangesAsync();
                }
            }
        }
    }
}