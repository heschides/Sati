using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Data.Cloud;
using Sati.Edi;
using Sati.Services.Billing;
using Sati.Services;
using Sati.Services.LocalAi;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.ViewModels.Admin;
using Sati.Views;
using Sati.Reporting;
using PdfSharp.Fonts;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

namespace Sati
{
    public partial class App : Application
    {
        private IHost? _host;
        private bool _isShowingUnhandledException;
        private bool _globalFailureHandlersRegistered;
        private readonly HashSet<string> _shownUnhandledExceptionFingerprints = new(StringComparer.Ordinal);
        public IServiceProvider Services => _host!.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            RegisterGlobalFailureHandlers();
            AppErrorLog.EnsureReady();

            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;

                // The distributable Demo build is intentionally cloud-only: it never
                // offers or resolves a direct SQL/LocalDB environment. Developer builds
                // retain the explicit environment chooser.
#if SATI_DEMO
                var selectedEnvironment = SatiDataEnvironment.Demo;
#else
                var environmentWindow = new DataEnvironmentWindow();
                if (environmentWindow.ShowDialog() != true ||
                    environmentWindow.SelectedEnvironment is not SatiDataEnvironment selectedEnvironment)
                {
                    Shutdown();
                    return;
                }
#endif

                _host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((_, configuration) =>
                    {
                        // Desktop launchers do not guarantee that the process working
                        // directory is the folder containing Sati.exe. Anchor all
                        // deployment configuration to the executable instead.
                        configuration.SetBasePath(AppContext.BaseDirectory);
                        configuration.AddJsonFile(
                            "appsettings.json",
                            optional: true,
                            reloadOnChange: false);

                        // Non-secret deployment endpoints are tracked separately
                        // from appsettings.json, which can contain a local Production
                        // connection string and is intentionally git-ignored.
                        configuration.AddJsonFile(
                            "appsettings.Public.json",
                            optional: false,
                            reloadOnChange: false);
                    })
                    .UseDefaultServiceProvider((_, options) =>
                    {
#if DEBUG
                        options.ValidateOnBuild = true;
                        options.ValidateScopes = true;
#endif
                    })
                    .ConfigureServices((context, services) =>
                    {
                        var dataEnvironment = DataEnvironmentResolver.Resolve(
                            context.Configuration,
                            selectedEnvironment);
                        services.AddSingleton(dataEnvironment);

                        services.Configure<LocalAiOptions>(
                            context.Configuration.GetSection(LocalAiOptions.SectionName));

                        // Services shared by both environments. Demo persistence is
                        // registered separately below and has no EF/SQL fallback.
                        services.AddTransient<IPasswordHasher, PasswordHasher>();
                        services.AddSingleton<ISessionService, SessionService>();
                        services.AddSingleton<IWindowsCrashEventReader, WindowsCrashEventReader>();
                        services.AddSingleton(serviceProvider => new ApplicationRunState(
                            serviceProvider.GetRequiredService<IWindowsCrashEventReader>()));
                        services.AddSingleton<IDatabaseActivityTracker, DatabaseActivityTracker>();
                        services.AddSingleton<DatabaseActivityViewModel>();
                        services.AddSingleton<DatabaseActivityPreview>();
                        services.AddSingleton<DatabaseActivityCommandInterceptor>();
                        services.AddTransient<DatabaseActivityHandler>();
                        services.AddTransient<IUpcomingEventService, UpcomingEventService>();
                        services.AddTransient<IWorkAgendaService, WorkAgendaService>();
                        services.AddTransient<DailyAgendaBuilder>();
                        services.AddSingleton<DailyAgendaCoordinator>();
                        services.AddSingleton<DailyAgendaLauncher>();
                        services.AddSingleton<ThemeService>();
                        services.AddSingleton<TextShortcutService>();
                        services.AddSingleton<DailyAgendaPreferenceService>();
                        services.AddSingleton<EasyEyesPreferenceService>();
                        services.AddSingleton<IdleLockPreferenceService>();
                        services.AddSingleton<ConsumerPickerSortPreferenceService>();
                        services.AddSingleton<IOutlookCalendarService, OutlookCalendarService>();
                        services.AddSingleton<IOutlookCalendarFilePicker, Sati.Views.OutlookCalendarFilePicker>();
                        services.AddSingleton<TextShortcutHook>();
                        services.AddSingleton<ICaseNoteFormatter, FoundryLocalCaseNoteFormatter>();

                        // Shared, not local-only: this is a pure renderer with no injected
                        // dependencies, and NewClientViewModel / ATRequestViewModel demand it
                        // in both environments. Registering it inside AddLocalDataServices
                        // made every Demo start fail service-provider validation.
                        services.AddSingleton<ATRequestPdfExporter>();
                        services.AddSingleton<Sati.Forms.AgencyReleasePdfGenerator>();
                        services.AddSingleton<Sati.Forms.MedicalReleasePdfGenerator>();

                        if (dataEnvironment.UsesCloudApi)
                            AddCloudDataServices(services, dataEnvironment);
                        else
                            AddLocalDataServices(services, dataEnvironment);
                        // Shell
                        services.AddSingleton<ShellViewModel>();
                        services.AddSingleton<ChatViewModel>();
                        services.AddSingleton<ShellWindow>();

                        // Child ViewModels
                        services.AddSingleton<CaseManagerDashboardViewModel>();
                        services.AddSingleton<StatisticsViewModel>();
                        services.AddTransient<ScratchpadViewModel>();
                        services.AddSingleton<GuidanceViewModel>();
                        services.AddSingleton<HelperReferenceViewModel>();
                        services.AddSingleton<ATRequestViewModel>();
                        services.AddSingleton<CaseManagementViewModel>();
                        services.AddSingleton<ProvidersViewModel>();
                        services.AddSingleton<ReviewsViewModel>();
                        services.AddSingleton<SupervisorDashboardViewModel>();
                        services.AddSingleton<AdminDashboardViewModel>();
                        services.AddSingleton<PlatformHealthViewModel>();
                        services.AddTransient<UserManagementViewModel>();
                        services.AddTransient<PendingApprovalsViewModel>();
                        services.AddTransient<CaseloadDistributionViewModel>();
                        services.AddTransient<ConsumerImportViewModel>();
                        services.AddTransient<CaseloadImportViewModel>();
                        services.AddSingleton<IClientExportReader, CredibleExportReader>();
                        services.AddSingleton<IExportFilePicker, Sati.Views.ExportFilePicker>();

                        // Modal windows and their ViewModels
                        services.AddTransient<LoginWindow>();
                        services.AddTransient<LoginWindowViewModel>();
                        services.AddTransient<NewUserWindow>();
                        services.AddTransient<FirstRunAdminWindow>();
                        services.AddTransient<FirstRunAdminViewModel>();
                        services.AddTransient<NewUserViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        services.AddTransient<SettingsWindow>();
                        services.AddTransient<DailyAgendaWindow>();
                        services.AddSingleton<NotesWindowViewModel>();
                        services.AddTransient<ComplianceReviewViewModel>();
                        services.AddTransient<ComplianceReviewWindow>();
                        services.AddTransient<ScratchpadHistoryViewModel>();
                        services.AddTransient<ScratchpadHistoryWindow>();
                        services.AddTransient<SwitchUserViewModel>();
                        services.AddTransient<SwitchUserWindow>();
                        services.AddTransient<MyAccountViewModel>();
                        services.AddTransient<DatabasePatienceWindow>();
                        services.AddTransient<SchedulerViewModel>();
                        services.AddTransient<SsnPanelViewModel>();
                        services.AddTransient<ConsumerProvidersViewModel>();
                        services.AddTransient<NewClientViewModel>();
                        services.AddTransient<ViewModels.ClientDocuments.DhhsFormsViewModel>();
                        services.AddTransient<ViewModels.ClientDocuments.AgencyReleaseViewModel>();
                        services.AddSingleton<Sati.Forms.DocumentTemplatePdfComposer>();
                        services.AddSingleton<Sati.Forms.SafetyPlanPdfGenerator>();
                        services.AddSingleton<Sati.Forms.DhhsFormFiller>();
                        services.AddSingleton<Sati.Forms.AnnualPacketComposer>();
                        services.AddTransient<ViewModels.ClientDocuments.SafetyPlanViewModel>();
                        services.AddTransient<ViewModels.ClientDocuments.AnnualDocumentsViewModel>();
                        services.AddTransient<ViewModels.ClientDocuments.SignatureRequestsViewModel>();
                        services.AddTransient<ViewModels.ClientDocuments.CheckRequestsViewModel>();
                        services.AddSingleton<CheckRequestPdfExporter>();

                        // Transient by intent: injected into two singleton hosts
                        // (CaseManagerDashboardViewModel, NotesWindowViewModel),
                        // each capturing its own long-lived, isolated instance.
                        services.AddTransient<NoteEntryViewModel>();
                        services.AddSingleton<BillingDashboardViewModel>();
                        services.AddSingleton<BillingOverviewViewModel>();
                        services.AddSingleton<BillingQueueViewModel>();
                        services.AddSingleton<BillingSubmissionsViewModel>();
                        services.AddSingleton<BillingRemittancesViewModel>();
                        services.AddSingleton<BillingAlertsViewModel>();
                        services.AddSingleton<CalendarViewModel>();

                        // Factories
                        services.AddTransient<Func<string, UserMessageDialog>>(sp => message => new UserMessageDialog(message));

                        // Asks before unsaved work is thrown away. Destructive
                        // styling, Cancel focused, Esc cancels — see ConfirmationDialog.
                        services.AddTransient<DiscardChangesPrompt>(sp => (title, message) =>
                        {
                            var dialog = new ConfirmationDialog(title, message, "Discard changes", isDestructive: true)
                            {
                                Owner = Application.Current.MainWindow
                            };
                            return dialog.ShowDialog() == true;
                        });
                        services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                        services.AddTransient<Func<DailyAgendaWindow>>(sp => () => sp.GetRequiredService<DailyAgendaWindow>());
                        services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                        services.AddTransient<Func<FirstRunAdminWindow>>(sp => () => sp.GetRequiredService<FirstRunAdminWindow>());
                        services.AddTransient<Func<ScratchpadHistoryWindow>>(sp => () => sp.GetRequiredService<ScratchpadHistoryWindow>());
                        services.AddTransient<Func<SwitchUserWindow>>(sp => () => sp.GetRequiredService<SwitchUserWindow>());
                        services.AddTransient<Func<LoginWindow>>(sp => () => sp.GetRequiredService<LoginWindow>());
                        services.AddTransient<Func<DatabasePatienceWindow>>(sp =>
                            () => sp.GetRequiredService<DatabasePatienceWindow>());
                    })
                    .Build();

                _host.Start();

                // Validate the database identity before migrations, authentication,
                // or any application query. A mismatched selection fails closed.
                var dataEnvironment = _host.Services.GetRequiredService<DataEnvironmentInfo>();
                if (!dataEnvironment.UsesCloudApi)
                {
                    using var provisioningScope = _host.Services.CreateScope();
                    await new LocalDatabaseProvisioner(
                        dataEnvironment,
                        provisioningScope.ServiceProvider.GetRequiredService<SatiContext>())
                        .ProvisionIfMissingAsync();
                    await _host.Services.GetRequiredService<DatabaseIdentityValidator>().ValidateAsync();
                }

                // Resolve before splash/login so the user's saved appearance is
                // applied to every window created during this session.
                _host.Services.GetRequiredService<ThemeService>();

                // The desktop owns Local Production migrations. The deployed API
                // validates Azure Demo and never lets the client mutate its schema.
                //
                // Backed by a backup on any database holding consumer records. This
                // runs before the splash screen on every machine that uses a local
                // database — including logins and laptops with real caseloads and
                // nobody present who could read a stack trace — so a failure has to
                // end in a sentence someone can act on, not an application that
                // silently refuses to start. See LocalDatabaseUpdater.
                if (!dataEnvironment.UsesCloudApi)
                {
                    using var scope = _host.Services.CreateScope();
                    var updater = new LocalDatabaseUpdater(
                        new SqlLocalDatabaseMaintenance(
                            scope.ServiceProvider.GetRequiredService<SatiContext>()));
                    var update = await updater.UpdateAsync();

                    // NeedsRepair is as much a stop as Failed: the schema is not what
                    // this build expects and starting anyway would run against a
                    // database it does not understand. It carries a different message
                    // because the reader can actually act on this one.
                    if (update.Outcome is LocalDatabaseUpdateOutcome.Failed
                                       or LocalDatabaseUpdateOutcome.NeedsRepair)
                    {
                        MessageBox.Show(
                            update.FailureMessage(),
                            "Sati cannot start",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }
                }

                // Login sequence
                var splash = new SplashScreenWindow();
                splash.Show();
                await Task.Delay(3000);
                splash.Close();

                var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
                loginWindow.Title = $"{dataEnvironment.WindowTitle} — Sign in";
                bool? result = loginWindow.ShowDialog();

                if (result == true)
                {
                    var user = loginWindow.LoggedInUser;
                    if (user == null) { Shutdown(); return; }

                    var session = _host.Services.GetRequiredService<ISessionService>();
                    session.SetUser(user);

                    if (_host.Services.GetService<IIncidentReporter>() is { } reporter)
                    {
                        await _host.Services.GetRequiredService<ApplicationRunState>()
                            .StartSessionAsync(user, reporter);
                        await reporter.FlushAsync();
                    }

                    var shellVm = _host.Services.GetRequiredService<ShellViewModel>();
                    await shellVm.InitializeAsync();

                    var shellWindow = _host.Services.GetRequiredService<ShellWindow>();
                    shellWindow.Title = dataEnvironment.WindowTitle;
                    shellWindow.Show();
                }
                else
                {
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                var reference = RecordAndReport(ex, "application.startup", IncidentSeverities.Critical);
                MessageBox.Show(
                    "Sati could not finish starting safely. No work session was opened. A diagnostic log was saved. " +
                    $"Please give support error reference {reference}.",
                    "Sati Could Not Start",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }

            base.OnStartup(e);
        }

        private void RegisterGlobalFailureHandlers()
        {
            if (_globalFailureHandlersRegistered)
                return;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _globalFailureHandlersRegistered = true;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            args.Handled = true;
            if (_isShowingUnhandledException)
            {
                Debug.WriteLine($"Suppressed reentrant UI exception: {args.Exception.GetType().FullName}");
                return;
            }

            var fingerprint = CreateExceptionFingerprint(args.Exception);
            if (!_shownUnhandledExceptionFingerprints.Add(fingerprint))
            {
                Debug.WriteLine($"Suppressed repeated UI exception: {fingerprint}");
                return;
            }

            _isShowingUnhandledException = true;
            try
            {
                var reference = RecordAndReport(args.Exception, "dispatcher.unhandled", IncidentSeverities.Error);
                MessageBox.Show(
                    "Sati encountered an unexpected problem. Your current action may not have completed. " +
                    $"A diagnostic log was saved. Please close and reopen Sati, and give support error reference {reference}.",
                    "Sati Could Not Complete the Action",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isShowingUnhandledException = false;
            }
        }

        private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            var exception = args.ExceptionObject as Exception ??
                new InvalidOperationException("A background thread terminated with a non-Exception failure object.");
            RecordAndReport(exception, "application.background-thread-unhandled", IncidentSeverities.Critical,
                waitBrieflyForReport: true);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            args.SetObserved();
            RecordAndReport(args.Exception, "application.unobserved-task", IncidentSeverities.Warning);
        }

        private string RecordAndReport(
            Exception exception,
            string operation,
            string severity,
            bool waitBrieflyForReport = false)
        {
            var reference = AppErrorLog.Record(exception, operation);
            try
            {
                if (_host?.Services.GetService<IIncidentReporter>() is not { } reporter)
                    return reference;

                var report = reporter.ReportAsync(exception, operation, reference, severity);
                if (waitBrieflyForReport)
                    report.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception reportingException)
            {
                Debug.WriteLine($"Sati incident {reference} could not be queued. Reporter failure type: {reportingException.GetType().FullName}");
            }
            return reference;
        }

        internal static string CreateExceptionFingerprint(Exception exception) => string.Join('|',
            exception.GetType().FullName,
            exception.HResult.ToString("X8"),
            exception.TargetSite?.DeclaringType?.FullName,
            exception.InnerException?.GetType().FullName,
            exception.InnerException?.HResult.ToString("X8"),
            exception.InnerException?.TargetSite?.DeclaringType?.FullName);

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_host is not null)
                {
                    await _host.StopAsync();
                    _host.Services.GetService<ApplicationRunState>()?.MarkGracefulExit();
                    _host.Dispose();
                }
            }
            finally
            {
                if (_globalFailureHandlersRegistered)
                {
                    DispatcherUnhandledException -= OnDispatcherUnhandledException;
                    AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
                    TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                    _globalFailureHandlersRegistered = false;
                }
            }

            base.OnExit(e);
        }

        private static void AddLocalDataServices(IServiceCollection services, DataEnvironmentInfo environment)
        {
            services.AddSingleton<DatabaseIdentityValidator>();
            services.AddTransient<IPersonService, PersonService>();
            services.AddTransient<IAdminService, AdminService>();
            services.AddTransient<ILegalHoldRegistry, LocalLegalHoldRegistry>();
            services.AddTransient<IIncidentReporter, LocalIncidentReporter>();
            services.AddTransient<IPlatformHealthService, LocalPlatformHealthService>();
            services.AddSingleton<PersonAuditPdfExporter>();
            services.AddTransient<IPersonContactService, PersonContactService>();
            services.AddTransient<IConsumerProviderService, ConsumerProviderService>();
            services.AddTransient<INoteService, NoteService>();
            services.AddTransient<IAuthService, AuthService>();
            services.AddTransient<IUserService, UserService>();
            services.AddSingleton<ISessionLifetime, NeverEndingSessionLifetime>();
            services.AddTransient<IScratchpadService, ScratchpadService>();
            services.AddSingleton<IChatService, ChatUnavailableService>();
            services.AddTransient<IIncentiveService, IncentiveService>();
            services.AddTransient<ISettingsService, SettingsService>();
            services.AddTransient<FormDueDateBackfill>();
            services.AddTransient<FormBulkCompletion>();
            services.AddTransient<IFormService, FormService>();
            services.AddTransient<ISupervisorService, SupervisorService>();
            services.AddTransient<IBillingService, BillingService>();
            services.AddTransient<IEdiService, EdiService>();
            services.AddTransient<IExemptDateService, ExemptDateService>();
            services.AddTransient<IConsumerBillingLossReportService, ConsumerBillingLossReportService>();
            services.AddTransient<IProductivityReportService, ProductivityReportService>();
            services.AddTransient<IReviewItemService, ReviewItemService>();
            services.AddSingleton<IClientAiContextService, ClientAiContextService>();
            services.AddTransient<IATRequestService, ATRequestService>();
            services.AddTransient<ICheckRequestService, CheckRequestService>();
            services.AddTransient<IProviderService, ProviderService>();
            services.AddTransient<IDhhsFormService, DhhsFormService>();
            // Local SSN protection: the same envelope the API uses, wrapped by the
            // Windows user account key instead of Key Vault. Singleton because none
            // of the three holds per-request state.
            services.AddSingleton<IKeyWrapper, DpapiKeyWrapper>();
            services.AddSingleton<EnvelopeProtector>();
            services.AddSingleton<LocalSsnStore>();
            services.AddTransient<IApiCompatibilityService, LocalApiCompatibilityService>();
            services.AddTransient<IAgencyReleaseService, AgencyReleaseService>();
            services.AddTransient<IDocumentTemplateService, DocumentTemplateService>();
            services.AddTransient<ISafetyPlanService, SafetyPlanService>();
            services.AddTransient<IAnnualDocumentService, AnnualDocumentService>();
            services.AddTransient<ISignatureService, SignatureUnavailableService>();
            services.AddTransient<IComprehensiveAssessmentService, ComprehensiveAssessmentService>();
            services.AddTransient<IPersonCenteredPlanSourceService, PersonCenteredPlanSourceService>();
            services.AddDbContextFactory<SatiContext>((serviceProvider, options) =>
                options
                    .UseSqlServer(environment.ConnectionString!)
                    .AddInterceptors(serviceProvider.GetRequiredService<DatabaseActivityCommandInterceptor>()),
                ServiceLifetime.Singleton);
        }

        private static void AddCloudDataServices(IServiceCollection services, DataEnvironmentInfo environment)
        {
            services.AddHttpClient("SatiDemo", client =>
            {
                client.BaseAddress = environment.ApiBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(90);
            }).AddHttpMessageHandler<DatabaseActivityHandler>();
            services.AddSingleton(sp => new CloudApiClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("SatiDemo")));

            // Only a token-bearing session can end or need renewing, so both live in
            // the cloud branch. Local Production gets the never-ending implementation
            // and no keep-alive at all.
            services.AddSingleton<ISessionLifetime, CloudSessionLifetime>();
            services.AddSingleton<SessionKeepAlive>();
            services.AddSingleton<IncidentOutbox>();
            services.AddTransient<IAuthService, CloudAuthService>();
            services.AddTransient<IAdminService, CloudAdminService>();
            services.AddTransient<IIncidentReporter, CloudIncidentReporter>();
            services.AddTransient<IPlatformHealthService, CloudPlatformHealthService>();
            services.AddTransient<IPersonService, CloudPersonService>();
            services.AddTransient<INoteService, CloudNoteService>();
            services.AddTransient<ISettingsService, CloudSettingsService>();
            services.AddTransient<IScratchpadService, CloudScratchpadService>();
            services.AddSingleton<IChatService, CloudChatService>();
            services.AddTransient<IExemptDateService, CloudExemptDateService>();
            services.AddTransient<IIncentiveService, CloudIncentiveService>();
            services.AddTransient<IFormService, CloudFormService>();
            services.AddTransient<IUserService, CloudUserService>();
            services.AddTransient<IPersonContactService, CloudPersonContactService>();
            services.AddTransient<IConsumerProviderService, CloudConsumerProviderService>();
            services.AddTransient<ISupervisorService, CloudSupervisorService>();
            services.AddTransient<IReviewItemService, CloudReviewItemService>();
            services.AddTransient<IATRequestService, CloudAtRequestService>();
            services.AddTransient<ICheckRequestService, CloudCheckRequestService>();
            services.AddTransient<IProviderService, CloudProviderService>();
            services.AddTransient<IDhhsFormService, CloudDhhsFormService>();
            services.AddTransient<IApiCompatibilityService, CloudApiCompatibilityService>();
            services.AddTransient<IAgencyReleaseService, CloudAgencyReleaseService>();
            services.AddTransient<IDocumentTemplateService, CloudDocumentTemplateService>();
            services.AddTransient<ISafetyPlanService, CloudSafetyPlanService>();
            services.AddTransient<IAnnualDocumentService, CloudAnnualDocumentService>();
            services.AddTransient<ISignatureService, CloudSignatureService>();
            services.AddTransient<IComprehensiveAssessmentService, CloudComprehensiveAssessmentService>();
            services.AddTransient<IPersonCenteredPlanSourceService, CloudPersonCenteredPlanSourceService>();
            services.AddTransient<IConsumerBillingLossReportService, CloudConsumerBillingLossReportService>();
            services.AddTransient<IProductivityReportService, CloudProductivityReportService>();
            services.AddTransient<IBillingService, CloudBillingService>();
            services.AddTransient<IEdiService, CloudEdiService>();
            services.AddSingleton<IClientAiContextService, CloudClientAiContextService>();
        }

    }
}
