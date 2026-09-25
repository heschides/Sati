using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels.Supervisor;
using System.Runtime.CompilerServices;
using System.Security;
using Xunit;

namespace Sati.Tests;

public sealed class SupervisorStartupPerformanceTests
{
    [Fact]
    public async Task DashboardLoadsOnceOnFirstUseAndReloadsAfterAccountSwitch()
    {
        var session = new SessionService();
        session.SetUser(Supervisor(8101, 81));
        var users = new CountingUserService();
        var settings = new CountingSettingsService();
        var dashboard = Dashboard(session, users, settings);

        Assert.False(dashboard.HasLoaded);
        Assert.Equal(0, users.SuperviseeLoads);

        await Task.WhenAll(dashboard.InitializeAsync(), dashboard.InitializeAsync());

        Assert.True(dashboard.HasLoaded);
        Assert.Equal(1, users.SuperviseeLoads);
        Assert.Equal(1, settings.Loads);

        await dashboard.InitializeAsync();
        Assert.Equal(1, users.SuperviseeLoads);

        var team = PrivateField<TeamOverviewViewModel>(dashboard, "_teamOverviewViewModel");
        var monthly = PrivateField<MonthlyProductivityViewModel>(dashboard, "_monthlyProductivityViewModel");
        monthly.Refresh(dashboard.CaseManagers);
        Assert.NotNull(team.ComplianceChartModel);
        Assert.NotNull(monthly.StatusChartModel);

        dashboard.ClearCharts();
        Assert.Null(team.ComplianceChartModel);
        Assert.Null(monthly.StatusChartModel);

        await dashboard.InitializeAsync();
        Assert.Equal(1, users.SuperviseeLoads);
        Assert.NotNull(team.ComplianceChartModel);
        Assert.NotNull(monthly.StatusChartModel);

        dashboard.ClearForAccountSwitch();
        session.SetUser(Supervisor(8102, 81));
        Assert.False(dashboard.HasLoaded);

        await dashboard.InitializeAsync();

        Assert.True(dashboard.HasLoaded);
        Assert.Equal(2, users.SuperviseeLoads);
        Assert.Equal(2, settings.Loads);
    }

    [Fact]
    public async Task ClearedAccountCannotPublishOrMarkItselfLoadedAfterItsRequestReturns()
    {
        var session = new SessionService();
        session.SetUser(Supervisor(8201, 82));
        var users = new CountingUserService { HoldFirstLoad = true };
        var dashboard = Dashboard(session, users, new CountingSettingsService());

        var oldLoad = dashboard.InitializeAsync();
        await users.FirstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        dashboard.ClearForAccountSwitch();
        session.SetUser(Supervisor(8202, 82));
        users.ReleaseFirstLoad.TrySetResult();
        await oldLoad;

        Assert.False(dashboard.HasLoaded);
        Assert.Empty(dashboard.CaseManagers);

        await dashboard.InitializeAsync();

        Assert.True(dashboard.HasLoaded);
        Assert.Equal(2, users.SuperviseeLoads);
    }

    private static SupervisorDashboardViewModel Dashboard(
        SessionService session,
        CountingUserService users,
        CountingSettingsService settings)
    {
        var userManagement = new UserManagementViewModel(users, session, () => null!);
        var pendingApprovals = new PendingApprovalsViewModel(null!, session);
        var checkRequests = new CheckRequestApprovalsViewModel(null!);
        var distribution = new CaseloadDistributionViewModel(null!, users, session);
        var import = new CaseloadImportViewModel(null!, null!, null!, session);
        var theme = (ThemeService)RuntimeHelpers.GetUninitializedObject(typeof(ThemeService));

        return new SupervisorDashboardViewModel(
            session,
            null!,
            null!,
            null!,
            settings,
            null!,
            users,
            theme,
            userManagement,
            pendingApprovals,
            checkRequests,
            distribution,
            import);
    }

    private static User Supervisor(int id, int agencyId) => User.Create(
        id,
        $"supervisor-{id}",
        $"Supervisor {id}",
        string.Empty,
        string.Empty,
        UserRole.Supervisor,
        supervisorId: null,
        agencyId);

    private static T PrivateField<T>(object instance, string name) where T : class =>
        (T)(instance.GetType().GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Missing private field {name}."));

    private sealed class CountingSettingsService : ISettingsService
    {
        private int _loads;
        public int Loads => Volatile.Read(ref _loads);

        public Task<Settings> LoadAsync()
        {
            Interlocked.Increment(ref _loads);
            return Task.FromResult(new Settings());
        }

        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class CountingUserService : IUserService
    {
        private int _superviseeLoads;
        public int SuperviseeLoads => Volatile.Read(ref _superviseeLoads);
        public bool HoldFirstLoad { get; init; }
        public TaskCompletionSource FirstLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<List<User>> GetSuperviseesAsync(int supervisorId)
        {
            var load = Interlocked.Increment(ref _superviseeLoads);
            if (load == 1)
            {
                FirstLoadStarted.TrySetResult();
                if (HoldFirstLoad)
                    await ReleaseFirstLoad.Task;
            }
            return [];
        }

        public Task<List<User>> GetAllAsync() => Task.FromResult(new List<User>());
        public Task<User> CreateAsync(AgencyActor actor, User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => Task.FromResult(false);
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(AgencyActor actor, User user) =>
            throw new NotSupportedException();
        public Task ResetPasswordAsync(AgencyActor actor, User user, SecureString newPassword) =>
            throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) =>
            throw new NotSupportedException();
    }
}
