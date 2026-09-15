using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class UserManagementPasswordResetTests
{
    [Fact]
    public async Task InvalidTemporaryPasswordExplainsWhyNothingWasChangedOnTheRenderedScreen()
    {
        var (viewModel, _) = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectUserCommand.Execute(viewModel.Users.Single(user => user.Username == "amber"));
        viewModel.ResetPasswordValue = Secure("short");
        viewModel.ResetPasswordConfirmation = Secure("short");

        await viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.Equal("Enter a new password between 8 and 128 characters.", viewModel.StatusMessage);
        WpfUiHarness.Run(() =>
        {
            var view = new UserManagementView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 900, 760);
            var status = WpfUiHarness.Descendants(view).OfType<TextBlock>()
                .Single(block => AutomationProperties.GetAutomationId(block) == "UserManagementStatus");
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal(viewModel.StatusMessage, AutomationProperties.GetName(status));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        });
    }

    [Fact]
    public async Task ValidTemporaryPasswordReachesTheSelectedUserAndConfirmsCompletion()
    {
        var (viewModel, service) = CreateViewModel();
        await viewModel.InitializeAsync();
        var amber = viewModel.Users.Single(user => user.Username == "amber");
        viewModel.SelectUserCommand.Execute(amber);
        viewModel.ResetPasswordValue = Secure("Temporary-Password-42!");
        viewModel.ResetPasswordConfirmation = Secure("Temporary-Password-42!");

        await viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.Same(amber, service.ResetTarget);
        Assert.Equal("Temporary-Password-42!", service.ResetPassword);
        Assert.Equal("Password reset for Amber Example.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AdministratorCanDisableEnableAndRevokeWithoutRemovingTheAccount()
    {
        var (viewModel, service) = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == 2);
        await viewModel.DisableAccountCommand.ExecuteAsync(null);
        Assert.False(viewModel.SelectedUser!.IsEnabled);
        Assert.Contains(viewModel.Users, user => user.Id == 2);
        Assert.Contains("disabled", viewModel.StatusMessage);
        Assert.False(viewModel.DisableAccountCommand.CanExecute(null));
        Assert.True(viewModel.EnableAccountCommand.CanExecute(null));
        await viewModel.EnableAccountCommand.ExecuteAsync(null);
        Assert.True(viewModel.SelectedUser!.IsEnabled);
        await viewModel.RevokeSessionsCommand.ExecuteAsync(null);
        Assert.Equal(2, service.EnableCalls);
        Assert.Equal(1, service.RevokeCalls);
        Assert.Contains("All sessions signed out", viewModel.StatusMessage);
        await viewModel.DisableAccountCommand.ExecuteAsync(null);
        Assert.Equal(3, service.EnableCalls);

        // Bind only after the command assertions finish. Bound WPF commands notify
        // dispatcher-owned buttons, so later test-thread commands would cross threads.
        WpfUiHarness.Run(() =>
        {
            var view = new UserManagementView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 1000, 1100);
            Assert.Contains(WpfUiHarness.Descendants(view).OfType<TextBlock>(),
                block => block.Visibility == Visibility.Visible && block.Text == "Disabled — sign-in blocked");
            var enable = WpfUiHarness.Descendants(view).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Enable account");
            Assert.Equal(Visibility.Visible, enable.Visibility);
            Assert.True(enable.IsEnabled);
        });
    }

    [Fact]
    public async Task SupervisorCannotInvokeLifecycleEvenDirectlyThroughTheCommand()
    {
        var (viewModel, service) = CreateViewModel(administrator: false);
        await viewModel.InitializeAsync();
        viewModel.SelectedUser = viewModel.Users.Single();
        Assert.False(viewModel.CanManageAccountLifecycle);
        Assert.False(viewModel.DisableAccountCommand.CanExecute(null));
        await viewModel.DisableAccountCommand.ExecuteAsync(null);
        await viewModel.RevokeSessionsCommand.ExecuteAsync(null);
        Assert.Equal(0, service.EnableCalls);
        Assert.Equal(0, service.RevokeCalls);
    }

    [Fact]
    public async Task AdministratorCannotDisableOwnAccountButCanRevokeOwnSessions()
    {
        var (viewModel, service) = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == 1);
        Assert.False(viewModel.DisableAccountCommand.CanExecute(null));
        await viewModel.DisableAccountCommand.ExecuteAsync(null);
        Assert.Equal(0, service.EnableCalls);
        await viewModel.RevokeSessionsCommand.ExecuteAsync(null);
        Assert.Equal(1, service.RevokeCalls);
    }

    private static (UserManagementViewModel ViewModel, CapturingUserService Service) CreateViewModel(bool administrator = true)
    {
        var admin = User.Create(1, "longchenpa", "Longchenpa", string.Empty, string.Empty,
            UserRole.Admin, null, 1);
        var amber = User.Create(2, "amber", "Amber Example", string.Empty, string.Empty,
            UserRole.CaseManager, 1, 1);
        if (!administrator) admin.Permissions = UserPermissions.Supervision;
        var session = new SessionService();
        session.SetUser(admin);
        var service = new CapturingUserService([admin, amber]);
        return (new UserManagementViewModel(
            service, session, () => throw new NotSupportedException()), service);
    }

    private static SecureString Secure(string value)
    {
        var secure = new SecureString();
        foreach (var character in value)
            secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }

    private sealed class CapturingUserService(List<User> users) : IUserService
    {
        public User? ResetTarget { get; private set; }
        public string? ResetPassword { get; private set; }
        public int EnableCalls { get; private set; }
        public int RevokeCalls { get; private set; }
        public Task SetEnabledAsync(AgencyActor actor, User user, bool enabled)
        {
            EnableCalls++;
            user.IsEnabled = enabled;
            return Task.CompletedTask;
        }
        public Task RevokeSessionsAsync(AgencyActor actor, User user)
        {
            RevokeCalls++;
            return Task.CompletedTask;
        }

        public Task<List<User>> GetAllAsync() => Task.FromResult(users);
        public Task<User> CreateAsync(AgencyActor actor, User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => throw new NotSupportedException();
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(AgencyActor actor, User user) =>
            throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) =>
            throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();

        public Task ResetPasswordAsync(AgencyActor actor, User user, SecureString newPassword)
        {
            ResetTarget = user;
            ResetPassword = new System.Net.NetworkCredential(string.Empty, newPassword).Password;
            return Task.CompletedTask;
        }
    }
}
