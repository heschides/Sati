using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Services.Billing;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
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
                    services.AddTransient<ISupervisorService, SupervisorService>();
                    services.AddTransient<IBillingService, BillingService>();
                    services.AddTransient<IEdiService, EdiService>();

                    // Shell
                    services.AddSingleton<ShellViewModel>();
                    services.AddSingleton<ShellWindow>();

                    // Child ViewModels
                    services.AddSingleton<CaseManagerDashboardViewModel>();
                    services.AddTransient<ScratchpadViewModel>();
                    services.AddSingleton<GuidanceViewModel>();
                    services.AddSingleton<HelpersViewModel>();
                    services.AddSingleton<SupervisorDashboardViewModel>();
                    services.AddTransient<UserManagementViewModel>();
                    services.AddTransient<PendingApprovalsViewModel>();


                    // Modal windows and their ViewModels
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<LoginWindowViewModel>();
                    services.AddTransient<NewUserWindow>();
                    services.AddTransient<NewUserViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<SettingsWindow>();
                    services.AddSingleton<NotesWindowViewModel>();
                    services.AddTransient<ComplianceReviewViewModel>();
                    services.AddTransient<ComplianceReviewWindow>();
                    services.AddTransient<ScratchpadHistoryViewModel>();
                    services.AddTransient<ScratchpadHistoryWindow>();
                    services.AddTransient<SwitchUserViewModel>();
                    services.AddTransient<SwitchUserWindow>();
                    services.AddTransient<SchedulerViewModel>();
                    services.AddTransient<NewClientViewModel>();

                    services.AddSingleton<BillingDashboardViewModel>();
                    services.AddSingleton<BillingOverviewViewModel>();
                    services.AddSingleton<BillingSubmissionsViewModel>();
                    services.AddSingleton<BillingRemittancesViewModel>();
                    services.AddSingleton<BillingAlertsViewModel>();

                    // Factories
                    services.AddTransient<Func<string, UserMessageDialog>>(sp => message => new UserMessageDialog(message));
                    services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                    services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                    services.AddTransient<Func<ScratchpadHistoryWindow>>(sp => () => sp.GetRequiredService<ScratchpadHistoryWindow>());
                    services.AddTransient<Func<SwitchUserWindow>>(sp => () => sp.GetRequiredService<SwitchUserWindow>());
                    // EF Core
                    services.AddDbContextFactory<SatiContext>(options =>
                        options.UseSqlServer(context.Configuration.GetConnectionString("SatiDb")),
                        ServiceLifetime.Singleton);
                })
                .Build();

            _host.Start();

            // Migrate database
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SatiContext>();
            db.Database.Migrate();
         //   await SeedMockDataAsync(_host.Services);

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

    //    private static async Task SeedMockDataAsync(IServiceProvider services)
    //    {
    //        using var scope = services.CreateScope();
    //        var context = scope.ServiceProvider.GetRequiredService<SatiContext>();
    //        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    //        // One-time guard — if Wolverine exists, we've already seeded
    //        if (await context.Users.AnyAsync(u => u.Username == "logan"))
    //            return;

    //        var random = new Random(42);
    //        var today = DateTime.Today;

    //        // -------------------------------------------------------------------------
    //        // Get Shantideva's ID for supervisor assignment
    //        // -------------------------------------------------------------------------
    //        var shantideva = await context.Users
    //            .FirstOrDefaultAsync(u => u.Username == "shantideva");
    //        int? supervisorId = shantideva?.Id;

    //        // -------------------------------------------------------------------------
    //        // Show rosters — 10 shows, 15 clients each
    //        // -------------------------------------------------------------------------
    //        var shows = new Dictionary<string, string[]>
    //        {
    //            ["House MD"] = new[]
    //            {
    //        "Gregory House", "James Wilson", "Lisa Cuddy", "Allison Cameron",
    //        "Robert Chase", "Eric Foreman", "Lawrence Kutner", "Chris Taub",
    //        "Remy Hadley", "Martha Masters", "Jessica Adams", "Chi Park",
    //        "Amber Volakis", "Edward Vogler", "Stacy Warner"
    //    },
    //            ["Cheers"] = new[]
    //            {
    //        "Sam Malone", "Diane Chambers", "Carla Tortelli", "Cliff Clavin",
    //        "Norm Peterson", "Ernie Pantusso", "Frasier Crane", "Lilith Sternin",
    //        "Rebecca Howe", "Woody Boyd", "Paul Krapence", "Phil Howser",
    //        "Al Rosen", "Linda Antonelli", "Henri Devereaux"
    //    },
    //            ["Night Court"] = new[]
    //            {
    //        "Harry Stone", "Dan Fielding", "Bull Shannon", "Mel Torme",
    //        "Roz Russell", "Christine Sullivan", "Mac Robinson", "Quon Le Duc",
    //        "Selma Hacker", "Buddy Ryan", "Tony Giuliano", "Marsha Donahue",
    //        "Bob Wheeler", "Lisette Hocheiser", "Yakov Korolenko"
    //    },
    //            ["Who's the Boss"] = new[]
    //            {
    //        "Tony Micelli", "Angela Bower", "Mona Robinson", "Jonathan Bower",
    //        "Samantha Micelli", "Billy Napier", "Kathleen Haskell", "Bonnie",
    //        "Jesse Riegert", "Andy Crenshaw", "Roger Carey", "Hank Bower",
    //        "Shirley Micelli", "Chuck", "Becky"
    //    },
    //            ["The Cosby Show"] = new[]
    //            {
    //        "Cliff Huxtable", "Clair Huxtable", "Denise Huxtable", "Theodore Huxtable",
    //        "Vanessa Huxtable", "Rudy Huxtable", "Sondra Huxtable", "Elvin Tibideaux",
    //        "Cockroach Williams", "Kenny Randolph", "Martin Kendall", "Pam Tucker",
    //        "Olivia Kendall", "Charmaine Brown", "Peter Chiara"
    //    },
    //            ["Slings & Arrows"] = new[]
    //            {
    //        "Geoffrey Tennant", "Ellen Fanshaw", "Richard Smith-Jones", "Darren Nichols",
    //        "Oliver Welles", "Anna Conroy", "Frank McKay", "Cyril Sinclair",
    //        "Kate McNab", "Maria Gillard", "Jerry Langdon", "Jack Crew",
    //        "Holly Day", "Henry Breedlove", "Charles Kingman"
    //    },
    //            ["Barry"] = new[]
    //            {
    //        "Barry Berkman", "Monroe Fuches", "Sally Reed", "NoHo Hank",
    //        "Gene Cousineau", "Cristobal Sifuentes", "Natalie Greer", "Albert Nguyen",
    //        "Leo Cousineau", "Fernando Groach", "Loach Moss", "Goran Milovic",
    //        "Esther Velencoso", "Taylor Garrett", "Jermaine Stewart"
    //    },
    //            ["The Drew Carey Show"] = new[]
    //            {
    //        "Drew Carey", "Mimi Bobeck", "Kate O'Brien", "Oswald Harvey",
    //        "Lewis Kiniski", "Mr. Wick", "Kellie Newmark", "Nicki Fifer",
    //        "Lisa Robbins", "Larry Almada", "Chuck Mitchell", "Winfred Ottman",
    //        "Steve Carey", "Carey Lewis", "Don Malone"
    //    },
    //            ["Scrubs"] = new[]
    //            {
    //        "John Dorian", "Christopher Turk", "Elliot Reid", "Carla Espinosa",
    //        "Perry Cox", "Glenn Matthews", "Jordan Sullivan", "Bob Kelso",
    //        "Laverne Roberts", "Todd Quinlan", "Doug Murphy", "Ted Buckland",
    //        "Kim Briggs", "Molly Clock", "Lloyd Braddock"
    //    },
    //            ["Family Ties"] = new[]
    //            {
    //        "Alex Keaton", "Mallory Keaton", "Jennifer Keaton", "Andy Keaton",
    //        "Skippy Handelman", "Nick Moore", "Lauren Miller", "Ellen Reed",
    //        "Steven Keaton", "Elyse Keaton", "Andrew Keaton", "Irwin Handelman",
    //        "Shelley Keaton", "Michael Gross", "Suzanne Valentine"
    //    }
    //        };

    //        // -------------------------------------------------------------------------
    //        // Case managers — superhero aliases, one per show
    //        // -------------------------------------------------------------------------
    //        var caseManagers = new[]
    //        {
      
    //("logan",        "James Logan",       "House MD"),
    //("ororo",        "Ororo Munroe",      "Cheers"),
    //("mattmurdock",  "Matt Murdock",      "Night Court"),
    //("natashar",     "Natasha Romanoff",  "Who's the Boss"),
    //("carllucas",    "Carl Lucas",        "The Cosby Show"),
    //("dannyrand",    "Danny Rand",        "Slings & Arrows"),
    //("dickgrayson",  "Dick Grayson",      "Barry"),
    //("jessicaj",     "Jessica Jones",     "The Drew Carey Show"),
    //("clintbarton",  "Clint Barton",      "Scrubs"),
    //("peterparkr",   "Peter Parker",      "Family Ties"),

    //    };

    //        var noteStatuses = new[]
    //        {
    //    NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged,
    //    NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged, NoteStatus.Logged,
    //    NoteStatus.Pending, NoteStatus.Pending, NoteStatus.Pending, NoteStatus.Pending,
    //    NoteStatus.Abandoned, NoteStatus.Abandoned, NoteStatus.Abandoned,
    //    NoteStatus.Scheduled, NoteStatus.Scheduled, NoteStatus.Scheduled,
    //    NoteStatus.Cancelled,
    //    NoteStatus.Delayed
    //};

    //        var noteTypes = new[]
    //        {
    //    NoteType.Visit, NoteType.Visit, NoteType.Visit, NoteType.Visit,
    //    NoteType.Visit, NoteType.Visit, NoteType.Visit,
    //    NoteType.Contact, NoteType.Contact, NoteType.Contact,
    //    NoteType.Other
    //};

    //        var narratives = new[]
    //        {
    //    "Conducted home visit. Consumer was cooperative and engaged.",
    //    "Completed monthly check-in. No concerns noted at this time.",
    //    "Reviewed service plan. Consumer expressed satisfaction with current supports.",
    //    "Attempted contact. Left voicemail, no response.",
    //    "Met with consumer and support agency to discuss service coordination.",
    //    "Consumer reported difficulty with transportation. Discussed alternatives.",
    //    "Completed documentation review. All forms current.",
    //    "Consumer attended day program. Staff reported positive engagement.",
    //    "Discussed upcoming 90-day review with consumer and guardian.",
    //    "Follow-up on previous contact. Issue resolved satisfactorily.",
    //    "Consumer requested change in service schedule. Documented and forwarded.",
    //    "Coordinated with medical provider regarding consumer health needs.",
    //    "Attended ISP meeting. Goals reviewed and updated.",
    //    "Consumer reported feeling well. No changes to service plan needed.",
    //    "Conducted compliance review. All documentation in order.",
    //};

    //        // -------------------------------------------------------------------------
    //        // Create users and their clients
    //        // -------------------------------------------------------------------------
    //        var (defaultHash, defaultSalt) = hasher.HashPassword("defaultpassword");

    //        foreach (var (username, displayName, show) in caseManagers)
    //        {
    //            // Create user
    //            var user = User.Create(
    //                id: 0,
    //                username: username,
    //                displayName: displayName,
    //                passwordHash: defaultHash,
    //                salt: defaultSalt,
    //                role: UserRole.CaseManager,
    //                supervisorId: supervisorId);

    //            context.Users.Add(user);
    //            await context.SaveChangesAsync();

    //            // Create clients from the assigned show
    //            var clients = shows[show];

    //            var bios = new Dictionary<string, string>
    //            {
    //                // House MD
    //                ["Gregory House"] = "Chronic pain management. Resistant to all treatment recommendations except his own.",
    //                ["James Wilson"] = "Referred by colleague. Strong social supports; perhaps too strong.",
    //                ["Lisa Cuddy"] = "Administrator type. Documents everything obsessively.",
    //                ["Allison Cameron"] = "High empathy, sometimes to a fault. Tendency to over-identify with consumers.",
    //                ["Robert Chase"] = "Australian national. Capable but defers to authority inappropriately.",
    //                ["Eric Foreman"] = "Self-sufficient and skeptical. Requires significant rapport-building.",
    //                ["Lawrence Kutner"] = "Enthusiastic. Prone to unconventional problem-solving.",
    //                ["Chris Taub"] = "Medical background. Questions every service plan decision.",
    //                ["Remy Hadley"] = "Private individual. Disclosure of diagnosis required significant time.",
    //                ["Martha Masters"] = "Highly verbal. Brings notecards to every appointment.",
    //                ["Jessica Adams"] = "Warm and engaged. Advocates strongly for self-determination.",
    //                ["Chi Park"] = "Quiet but observant. Prefers written communication over phone.",
    //                ["Amber Volakis"] = "Competitive by nature. Sets her own goals before you can.",
    //                ["Edward Vogler"] = "Former executive. Struggles with loss of control over environment.",
    //                ["Stacy Warner"] = "Legal background. Reviews all documentation before signing.",

    //                // Cheers
    //                ["Sam Malone"] = "Recovering alcoholic. Strong community ties; bar is a risk factor.",
    //                ["Diane Chambers"] = "Graduate student. Verbose in assessments. Brings bibliography.",
    //                ["Carla Tortelli"] = "Multiple dependents. Highly resourceful under pressure.",
    //                ["Cliff Clavin"] = "Lives with mother. Extensive knowledge of obscure regulations.",
    //                ["Norm Peterson"] = "Unemployed. Reported support network consists primarily of bar stool.",
    //                ["Ernie Pantusso"] = "Mild cognitive concerns. Beloved by all who meet him.",
    //                ["Frasier Crane"] = "Psychiatrist on leave. Will analyze your intervention before accepting it.",
    //                ["Lilith Sternin"] = "Highly structured routine. Responds poorly to unannounced visits.",
    //                ["Rebecca Howe"] = "Recent career transition. Aspirational goals; inconsistent follow-through.",
    //                ["Woody Boyd"] = "Rural background. Earnest and cooperative. Excellent ISP participant.",
    //                ["Paul Krapence"] = "Frequent bar patron. Limited social circle outside the establishment.",
    //                ["Phil Howser"] = "Quiet regular. Difficult to distinguish from the furniture.",
    //                ["Al Rosen"] = "Retired. Reliable attendee. Hasn't missed an appointment in four years.",
    //                ["Linda Antonelli"] = "Part-time employee. Scheduling requires flexibility.",
    //                ["Henri Devereaux"] = "French national. Charming but evasive during goal-setting.",

    //                // Night Court
    //                ["Harry Stone"] = "Unconventional problem-solver. Owns extensive Mel Tormé memorabilia.",
    //                ["Dan Fielding"] = "Charm-based coping mechanisms. Not recommended for group settings.",
    //                ["Bull Shannon"] = "Large frame; gentle demeanor. Responds well to structured environments.",
    //                ["Mel Torme"] = "Celebrity referral. Cooperative but expects special accommodations.",
    //                ["Roz Russell"] = "Highly competent. Will run the meeting if you let her.",
    //                ["Christine Sullivan"] = "Public defender background. Advocates aggressively for her own services.",
    //                ["Mac Robinson"] = "Organized and reliable. Keeps better records than the agency.",
    //                ["Quon Le Duc"] = "Recent immigrant. Language supports in place. Strong family network.",
    //                ["Selma Hacker"] = "Veteran employee. Set in routines. Resistant to new service delivery models.",
    //                ["Buddy Ryan"] = "Former law enforcement. Cooperative with structure. Dislikes ambiguity.",
    //                ["Tony Giuliano"] = "Court regular. Familiar with the intake process.",
    //                ["Marsha Donahue"] = "Reliable attendee. Brings snacks to team meetings.",
    //                ["Bob Wheeler"] = "Rural consumer. Long travel times for appointments.",
    //                ["Lisette Hocheiser"] = "Interested in independence. Employment goals identified.",
    //                ["Yakov Korolenko"] = "Russian emigrant. Dry sense of humor. Thrives with a consistent case manager.",

    //                // Who's the Boss
    //                ["Tony Micelli"] = "Former baseball player. Strong work ethic; pride occasionally impedes help-seeking.",
    //                ["Angela Bower"] = "Advertising executive. High-functioning; took time to acknowledge need for support.",
    //                ["Mona Robinson"] = "Older adult. Independent and spirited. Resists any suggestion otherwise.",
    //                ["Jonathan Bower"] = "Youth services transition case. Academic focus.",
    //                ["Samantha Micelli"] = "Adolescent transition planning. Self-directed and motivated.",
    //                ["Billy Napier"] = "Peer of primary consumer. Incidental referral.",
    //                ["Kathleen Haskell"] = "Neighbor referral. Isolated; slowly building community connections.",
    //                ["Bonnie"] = "Last name unknown. Records incomplete. Doing fine, apparently.",
    //                ["Jesse Riegert"] = "Employment supports active. Strong vocational history.",
    //                ["Andy Crenshaw"] = "Youth. Energetic. Difficult to keep seated during assessments.",
    //                ["Roger Carey"] = "Adult male. Referred by physician. Cooperative.",
    //                ["Hank Bower"] = "Extended family member. Occasional participant in family meetings.",
    //                ["Shirley Micelli"] = "Out-of-state family. Phone check-ins only.",
    //                ["Chuck"] = "Last name unknown. Goes by Chuck. Pleasant.",
    //                ["Becky"] = "Last name unknown. Friend of consumer. Tangential referral.",

    //                // The Cosby Show
    //                ["Cliff Huxtable"] = "OB/GYN. Convinced he already knows the best intervention.",
    //                ["Clair Huxtable"] = "Attorney. Thoroughly reviews all service agreements.",
    //                ["Denise Huxtable"] = "Creative type. Goals shift frequently; engagement is genuine.",
    //                ["Theodore Huxtable"] = "Developing young adult. Needs encouragement with follow-through.",
    //                ["Vanessa Huxtable"] = "Middle child. Motivated by fairness; escalates quickly if overlooked.",
    //                ["Rudy Huxtable"] = "Youth services. Charming. Gets away with everything.",
    //                ["Sondra Huxtable"] = "Princeton graduate. High expectations of service quality.",
    //                ["Elvin Tibideaux"] = "Married into the caseload. Still adjusting to help-seeking.",
    //                ["Cockroach Williams"] = "Self-selected nickname. Academic supports identified.",
    //                ["Kenny Randolph"] = "Goes by Bud. Loyal support network; one primary friend.",
    //                ["Martin Kendall"] = "Son-in-law. Quiet. Agrees with whatever Sondra says.",
    //                ["Pam Tucker"] = "Cousin referral. Streetwise; rapport built quickly.",
    //                ["Olivia Kendall"] = "Young child. Extremely talkative. Assessment took three sessions.",
    //                ["Charmaine Brown"] = "Friend of family. Peripheral participant.",
    //                ["Peter Chiara"] = "Occasional visitor. Involvement inconsistent.",

    //                // Slings & Arrows
    //                ["Geoffrey Tennant"] = "Artistic director. Brilliant, erratic. Talks to his deceased mentor.",
    //                ["Ellen Fanshaw"] = "Actress. Highly emotional processing style. Very committed to narrative.",
    //                ["Richard Smith-Jones"] = "Festival administrator. Risk-averse. Documents extensively.",
    //                ["Darren Nichols"] = "Director. Avant-garde approach to all life domains, including service planning.",
    //                ["Oliver Welles"] = "Deceased. File retained for historical reference.",
    //                ["Anna Conroy"] = "Stage manager. Reliable. Will fix whatever you forget to do.",
    //                ["Frank McKay"] = "Union rep. Knows his rights. Will cite them.",
    //                ["Cyril Sinclair"] = "Board member. Interested in outcomes data.",
    //                ["Kate McNab"] = "Eager to please; working on self-advocacy.",
    //                ["Maria Gillard"] = "Administrative support. Organized. Arrived with her own agenda.",
    //                ["Jerry Langdon"] = "Technical crew. Pragmatic. Wants solutions, not process.",
    //                ["Jack Crew"] = "Actor. Handsome. Aware of it.",
    //                ["Holly Day"] = "Wardrobe. Observant. Noticed the stain on your tie.",
    //                ["Henry Breedlove"] = "Investor. Wants ROI on service delivery.",
    //                ["Charles Kingman"] = "Board chair. Rarely present. Highly opinionated remotely.",

    //                // Barry
    //                ["Barry Berkman"] = "Veteran. Complex trauma history. Do not ask about his hobbies.",
    //                ["Monroe Fuches"] = "Enabling relationship dynamics clearly identified.",
    //                ["Sally Reed"] = "Aspiring actor. Narrative processing is her primary coping tool.",
    //                ["NoHo Hank"] = "Eastern European national. Cheerful affect incongruous with life circumstances.",
    //                ["Gene Cousineau"] = "Acting teacher. Centers himself in every conversation.",
    //                ["Cristobal Sifuentes"] = "Working toward prosocial goals. Progress is real.",
    //                ["Natalie Greer"] = "Support person. Quietly competent. Possibly the most functional person here.",
    //                ["Albert Nguyen"] = "Law enforcement background. Persistent. Very persistent.",
    //                ["Leo Cousineau"] = "Son of primary consumer. Complicated family dynamics.",
    //                ["Fernando Groach"] = "Associate of consumer. Brief involvement.",
    //                ["Loach Moss"] = "Detective. Investigating something. Unclear if client or not.",
    //                ["Goran Milovic"] = "Chechen national. Communication barriers present. Very friendly.",
    //                ["Esther Velencoso"] = "Strong leadership skills in prior setting. Transitioning.",
    //                ["Taylor Garrett"] = "Former associate. Transitioning to legitimate employment.",
    //                ["Jermaine Stewart"] = "Acting class attendee. Peripheral involvement.",

    //                // The Drew Carey Show
    //                ["Drew Carey"] = "Retail manager. Stress-related referral. Supportive friend group is an asset.",
    //                ["Mimi Bobeck"] = "Coworker of primary consumer. Adversarial history; slowly thawing.",
    //                ["Kate O'Brien"] = "Friend of consumer. Healthy attachment. Good influence.",
    //                ["Oswald Harvey"] = "Delivery driver. Easygoing. Easily led.",
    //                ["Lewis Kiniski"] = "Underemployed. Creative solutions to financial instability.",
    //                ["Mr. Wick"] = "Supervisor of primary consumer. Not a client; keeps ending up in notes.",
    //                ["Kellie Newmark"] = "Girlfriend of consumer. Warm support system.",
    //                ["Nicki Fifer"] = "Ex-girlfriend. Less warm.",
    //                ["Lisa Robbins"] = "Healthcare worker. Referred by physician colleague.",
    //                ["Larry Almada"] = "Bar regular. Limited broader social engagement.",
    //                ["Chuck Mitchell"] = "Coworker. Peripheral.",
    //                ["Winfred Ottman"] = "Executive. High stress. Reluctant participant in services.",
    //                ["Steve Carey"] = "Brother of primary consumer. Occasional involvement.",
    //                ["Carey Lewis"] = "Naming conventions unclear. Possible duplicate record.",
    //                ["Don Malone"] = "Bar patron. Consistent presence. No identified goals.",

    //                // Scrubs
    //                ["John Dorian"] = "Referred as J.D. Daydreams extensively. Genuine warmth underneath.",
    //                ["Christopher Turk"] = "Surgeon. Competitive. Best friend is both asset and distraction.",
    //                ["Elliot Reid"] = "Physician. High-achieving. Occasional spiral under pressure.",
    //                ["Carla Espinosa"] = "Nurse. Runs the unit whether titled to or not.",
    //                ["Perry Cox"] = "Attending physician. Hostile presentation masks genuine investment.",
    //                ["Glenn Matthews"] = "Clarified he has his own name and would like it used.",
    //                ["Jordan Sullivan"] = "Ex-wife of consumer. Involved. Very involved.",
    //                ["Bob Kelso"] = "Chief of medicine. Appears callous. Occasionally isn't.",
    //                ["Laverne Roberts"] = "Veteran nurse. Faith-based coping. Sound judgment.",
    //                ["Todd Quinlan"] = "Surgeon. Maturational concerns. High-fives everything.",
    //                ["Doug Murphy"] = "Pathologist. Better with deceased than living. Working on it.",
    //                ["Ted Buckland"] = "Attorney. Clinically depressed. Self-aware about it.",
    //                ["Kim Briggs"] = "OB. Co-parenting with primary consumer. Complicated.",
    //                ["Molly Clock"] = "Psychiatrist. Cheerful. Unsettlingly cheerful.",
    //                ["Lloyd Braddock"] = "Delivery person. Knows everyone. Surprisingly resourceful.",

    //                // Family Ties
    //                ["Alex Keaton"] = "Young Republican. Highly motivated. Goal-oriented to a fault.",
    //                ["Mallory Keaton"] = "Fashion-focused. Stronger than she presents.",
    //                ["Jennifer Keaton"] = "Athletic. Independent. Low support needs.",
    //                ["Andy Keaton"] = "Young child. Precocious. Derails meetings with philosophical questions.",
    //                ["Skippy Handelman"] = "Neighbor. Devoted to consumer family. Boundaries being developed.",
    //                ["Nick Moore"] = "Artist. Free spirit. No discernible plan.",
    //                ["Lauren Miller"] = "Alex's girlfriend. Pre-med. High-achieving peer support.",
    //                ["Ellen Reed"] = "Family friend. Peripheral involvement.",
    //                ["Steven Keaton"] = "PBS station manager. Values-driven decision-making.",
    //                ["Elyse Keaton"] = "Architect. Creative. Strong family anchor.",
    //                ["Andrew Keaton"] = "Younger child. Arrived late. Still catching up.",
    //                ["Irwin Handelman"] = "Skippy's father. Rarely mentioned. Included for completeness.",
    //                ["Shelley Keaton"] = "Extended family. Visited once. Left an impression.",
    //                ["Michael Gross"] = "Appears to be the actor's name. Records may be misfiled.",
    //                ["Suzanne Valentine"] = "Friend of family. Positive influence. Limited involvement.",
    //            };

    //            foreach (var clientName in clients)
    //            {
    //                var nameParts = clientName.Split(' ', 2);
    //                var firstName = nameParts[0];
    //                var lastName = nameParts.Length > 1 ? nameParts[1] : "Unknown";

    //                // Random effective date between 1 and 4 years ago
    //                var daysAgo = random.Next(365, 365 * 4);
    //                var effectiveDate = today.AddDays(-daysAgo);

    //                // Random birthdate between 25 and 65 years ago
    //                var birthDaysAgo = random.Next(365 * 25, 365 * 65);
    //                var birthDate = today.AddDays(-birthDaysAgo);

    //                var person = Person.CreatePerson(
    //                    userId: user.Id,
    //                    firstName: firstName,
    //                    lastName: lastName,
    //                    bio: bios.TryGetValue(clientName, out var clientBio) ? clientBio : $"Consumer enrolled in {show} waiver program.", birthdate: birthDate,
    //                    effective: effectiveDate,
    //                    waiver: random.Next(2) == 0 ? WaiverType.Section21 : WaiverType.Section29);

    //                context.People.Add(person);
    //                await context.SaveChangesAsync();

    //                // Create 20 notes per client
    //                for (int i = 0; i < 20; i++)
    //                {
    //                    var status = noteStatuses[random.Next(noteStatuses.Length)];
    //                    var noteType = noteTypes[random.Next(noteTypes.Length)];
    //                    var narrative = narratives[random.Next(narratives.Length)];

    //                    // Random event date within the last 90 days
    //                    var eventDate = today.AddDays(-random.Next(0, 90));

    //                    var note = Note.Create(
    //                        narrative: narrative,
    //                        eventDate: eventDate,
    //                        status: status,
    //                        unitCount: random.Next(1, 8),
    //                        personId: person.Id,
    //                        noteType: noteType);

    //                    context.Notes.Add(note);
    //                }

    //                await context.SaveChangesAsync();
    //            }
    //       }
    //   }
    }
}