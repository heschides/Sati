using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Contracts.V1;
using System.Security;

namespace Sati.Data
{
    public class UserService : IUserService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly IPasswordHasher _hasher;
        private readonly ISessionService? _sessionService;

        public UserService(IDbContextFactory<SatiContext> contextFactory, IPasswordHasher hasher,
            ISessionService? sessionService = null)
        {
            _contextFactory = contextFactory;
            _hasher = hasher;
            _sessionService = sessionService;
        }

        // Authorization lives here, in the write, not in the view model that opened the
        // window. On local Production there is no API behind this method, so a permission
        // checkbox bound to a view-model boolean is the whole enforcement if this does not
        // check — which is what the 2026-08-30 audit found. The rule itself is owned by
        // Sati.Contracts.V1.UserManagementRules and shared with Sati.Api, so the two cannot
        // drift apart.
        public async Task<User> CreateAsync(AgencyActor suppliedActor, User user, SecureString initialPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            var actor = await ValidateActorAsync(context, suppliedActor);

            // Assigned server-side rather than trusted, matching POST /api/v1/users.
            user.AgencyId = actor.AgencyId;
            Refuse(UserManagementRules.DescribeGrantRefusal(
                actor.ToAgencyActor(), user.Permissions, user.SupervisorId, user.AgencyId));

            // The label is derived, never taken from the caller, so no user-management
            // path can mint the cross-tenant PlatformOperator identity.
            user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(user.Permissions));

            if (await context.Users.AnyAsync(candidate => candidate.Username == user.Username))
                throw new InvalidOperationException($"A user named '{user.Username}' already exists.");
            await RequireValidSupervisorAsync(context, actor, user.SupervisorId);

            var (hash, salt) = _hasher.HashPassword(initialPassword);
            user.SetPassword(hash, salt);
            user.IsEnabled = true;
            user.SecurityVersion = 1;
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> AnyAdministratorExistsAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Users.AnyAsync(user =>
                (user.Permissions & UserPermissions.Administration) != 0);
        }

        // The bootstrap window, and the thing that closes it.
        //
        // The existence check happens HERE, against the database, immediately
        // before the insert — not in the view model that opened the window. A
        // caller that checked first and acted later would be acting on a fact that
        // may have changed, and the check that matters is the one the write itself
        // performs.
        //
        // The role is forced rather than trusted from the incoming user, so this
        // path can only ever produce an administrator. Producing something else
        // would leave the installation still without one and the window still open.
        public async Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            if (await context.Users.AnyAsync(candidate =>
                    (candidate.Permissions & UserPermissions.Administration) != 0))
                throw new AdministratorAlreadyExistsException();

            if (await context.Users.AnyAsync(candidate => candidate.Username == user.Username))
                throw new InvalidOperationException($"A user named '{user.Username}' already exists.");

            user.AgencyId = await ResolveBootstrapAgencyIdAsync(context);

            var (hash, salt) = _hasher.HashPassword(initialPassword);
            user.SetPassword(hash, salt);
            // Forced rather than trusted from the caller: this path exists to end
            // the state of having no administrator, and anything else would leave
            // that state intact with the window still open.
            user.Role = UserRole.Admin;
            user.Permissions = UserPermissions.AllAgencyPermissions;
            user.IsEnabled = true;
            user.SecurityVersion = 1;
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user;
        }

        /// <summary>
        /// Which agency the first administrator belongs to.
        ///
        /// Every Sati database ships with two seeded agencies ("Internal" and
        /// "Sandbox Mode"), so simply picking the only one is not available. The
        /// answer that is actually right is THE AGENCY THE EXISTING PEOPLE ARE IN:
        /// an administrator exists to administer the agency that holds the work,
        /// and on the installation this feature was built for that is a single
        /// case manager sitting in one of them.
        ///
        /// PlatformOperator accounts are excluded because they are Sati's own
        /// cross-tenant identity and their agency says nothing about which tenant
        /// needs administering.
        ///
        /// Genuine ambiguity — real users spread across several agencies — is
        /// reported rather than guessed at. Attaching the only account that can
        /// administer the system to the wrong tenant is not a mistake that
        /// announces itself afterwards.
        /// </summary>
        private static async Task<int> ResolveBootstrapAgencyIdAsync(SatiContext context)
        {
            var occupiedAgencyIds = await context.Users
                .Where(user => user.Role != UserRole.PlatformOperator)
                .Select(user => user.AgencyId)
                .Distinct()
                .Take(2)
                .ToListAsync();

            if (occupiedAgencyIds.Count == 1)
                return occupiedAgencyIds[0];

            if (occupiedAgencyIds.Count > 1)
                throw new InvalidOperationException(
                    "This database has users in more than one agency, so the administrator's agency is ambiguous. " +
                    "Provision the administrator with the provisioning script, which takes the agency explicitly.");

            // A genuinely empty installation. Fall back to the lowest seeded
            // agency, which is the primary one rather than the sandbox.
            var agencyId = await context.Agencies
                .OrderBy(agency => agency.Id)
                .Select(agency => (int?)agency.Id)
                .FirstOrDefaultAsync();

            return agencyId ?? throw new InvalidOperationException(
                "This database has no agency, so an administrator cannot be attached to one.");
        }

        public async Task<List<User>> GetAllAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await RequireSessionAsync(context);
            var users = await LoadSafeUsersAsync(context, context.Users.AsNoTracking()
                .Where(user => user.AgencyId == actor.AgencyId && user.Role != UserRole.PlatformOperator));
            foreach (var user in users)
                user.Supervisees = users.Where(candidate => candidate.SupervisorId == user.Id).ToList();
            return users;
        }

        public async Task UpdateAsync(AgencyActor suppliedActor, User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var actor = await ValidateActorAsync(context, suppliedActor);

            var tracked = await context.Users.FindAsync(user.Id);
            if (tracked is null)
                return;
            RequireManageable(actor, tracked);
            Refuse(UserManagementRules.DescribeGrantRefusal(
                actor.ToAgencyActor(), user.Permissions, user.SupervisorId, tracked.AgencyId));
            await RequireValidSupervisorAsync(context, actor, user.SupervisorId);

            // Agency and derived label are set here rather than accepted, so neither a stale
            // in-memory object nor a caller can move a user across tenants or relabel one.
            user.AgencyId = tracked.AgencyId;
            user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(user.Permissions));

            // Account state and credentials are never accepted through an ordinary
            // profile save, including an old profile opened before a revocation.
            context.Entry(tracked).Property(item => item.Username).CurrentValue = user.Username;
            context.Entry(tracked).Property(item => item.DisplayName).CurrentValue = user.DisplayName;
            tracked.Permissions = user.Permissions;
            tracked.Role = user.Role;
            tracked.SupervisorId = user.SupervisorId;
            tracked.Email = user.Email;
            tracked.Phone = user.Phone;
            await SaveAccountChangesAsync(context);
            await transaction.CommitAsync();
        }

        // Self-service profile edit. Deliberately not UpdateAsync with a relaxed rule: this
        // takes the two fields a user may change about themselves and cannot express a
        // permission, agency, supervisor, or label change at all.
        public async Task UpdateOwnContactDetailsAsync(AgencyActor suppliedActor, User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            if (user.Id != suppliedActor.UserId)
                throw new UnauthorizedAccessException("You may edit only your own profile.");
            if (user.Email?.Length > 254)
                throw new InvalidOperationException("Email must not exceed 254 characters.");
            if (user.Phone?.Length > 30)
                throw new InvalidOperationException("Phone must not exceed 30 characters.");

            await using var context = _contextFactory.CreateDbContext();
            // Not ValidateActorAsync: editing your own contact details is not a user-management
            // action and must not require supervision or administration.
            if (_sessionService is not null)
            {
                var signedIn = await RequireSessionAsync(context);
                if (signedIn.Id != suppliedActor.UserId || signedIn.SecurityVersion != suppliedActor.SecurityVersion)
                    throw new UnauthorizedAccessException("The actor does not match the signed-in account.");
            }
            var tracked = await context.Users.SingleOrDefaultAsync(candidate =>
                              candidate.Id == suppliedActor.UserId)
                          ?? throw new UnauthorizedAccessException(
                              "The actor no longer matches the current user record.");
            EnsureLiveActor(tracked, suppliedActor);
            if (tracked.AgencyId != suppliedActor.AgencyId || tracked.Permissions != suppliedActor.Permissions)
                throw new UnauthorizedAccessException("The actor no longer matches the current user record.");

            // Only these two fields are copied. Permissions, Role, SupervisorId, and AgencyId
            // on the incoming object are ignored rather than trusted.
            tracked.Email = user.Email;
            tracked.Phone = user.Phone;
            await SaveAccountChangesAsync(context);
        }

        public async Task ResetPasswordAsync(AgencyActor suppliedActor, User user, SecureString newPassword)
        {
            ArgumentNullException.ThrowIfNull(user);

            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var actor = await ValidateActorAsync(context, suppliedActor);

            var tracked = await context.Users.FindAsync(user.Id)
                ?? throw new InvalidOperationException("The user no longer exists.");
            RequireManageable(actor, tracked);

            var (hash, salt) = _hasher.HashPassword(newPassword);
            var previousSecurityVersion = tracked.SecurityVersion;
            tracked.SetPassword(hash, salt);
            tracked.SecurityVersion = AccountSessionRules.NextSecurityVersion(tracked.SecurityVersion);
            LocalAuditTrail.Record(context, actor, "user.password-reset", "User", tracked.Id);
            await SaveAccountChangesAsync(context);
            await transaction.CommitAsync();
            InvalidateMatchingSession(user.Id, previousSecurityVersion);
        }

        /// <summary>
        /// Re-confirms a caller-supplied actor against the database. Mirrors
        /// <c>ValidateBillingActorAsync</c> and the API's <c>ValidatedActorFilter</c>: a
        /// supplied permission set is never trusted, only matched.
        /// </summary>
        private async Task<User> ValidateActorAsync(SatiContext context, AgencyActor suppliedActor)
        {
            if (_sessionService is not null)
            {
                var signedIn = await RequireSessionAsync(context);
                if (signedIn.Id != suppliedActor.UserId || signedIn.SecurityVersion != suppliedActor.SecurityVersion)
                    throw new UnauthorizedAccessException("The actor does not match the signed-in account.");
            }
            if (!UserPermissionRules.IsSupported(suppliedActor.Permissions) ||
                !UserManagementRules.CanManageUsers(suppliedActor.Permissions))
                throw new UnauthorizedAccessException(UserManagementRules.RequiresUserManagement);

            var actor = await context.Users.SingleOrDefaultAsync(candidate =>
                       candidate.Id == suppliedActor.UserId)
                   ?? throw new UnauthorizedAccessException(
                       "The actor no longer matches the current user record.");
            EnsureLiveActor(actor, suppliedActor);
            if (actor.AgencyId != suppliedActor.AgencyId || actor.Permissions != suppliedActor.Permissions)
                throw new UnauthorizedAccessException("The actor no longer matches the current user record.");
            return actor;
        }

        // What the actor may act ON, as opposed to what they may grant. Mirrors the
        // same-agency, no-PlatformOperator, assigned-case-manager checks on
        // PUT /api/v1/users/{userId}.
        private static void RequireManageable(User actor, User target)
        {
            Refuse(UserManagementRules.DescribeTargetRefusal(actor.ToAgencyActor(), target.Permissions,
                target.SupervisorId, target.AgencyId, target.Role.ToString()));
        }

        private static async Task RequireValidSupervisorAsync(SatiContext context, User actor, int? supervisorId)
        {
            if (!supervisorId.HasValue)
                return;
            if (!await context.Users.AsNoTracking().AnyAsync(candidate =>
                    candidate.Id == supervisorId && candidate.AgencyId == actor.AgencyId &&
                    (candidate.Permissions & UserPermissions.Supervision) != 0))
                throw new InvalidOperationException("The selected supervisor is invalid.");
        }

        private static void Refuse(UserManagementRules.Refusal? refusal)
        {
            if (refusal is not null)
                throw new UnauthorizedAccessException(refusal.Message);
        }

        // Verify the current live sign-in and password here, then atomically save
        // the replacement verifier, advance the session version, and record the
        // security event. Never refresh the caller's old session stamp in place.
        public async Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword)
        {
            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (_sessionService is not null)
            {
                var captured = await RequireSessionAsync(context);
                if (captured.Id != user.Id || captured.SecurityVersion != user.SecurityVersion)
                    throw new UnauthorizedAccessException("Only the signed-in user may change this password.");
            }
            var tracked = await context.Users.FindAsync(user.Id)
                ?? throw new InvalidOperationException("The current user no longer exists.");
            EnsureLiveActor(tracked, user.ToAgencyActor());
            if (!_hasher.Verify(currentPassword, tracked.PasswordHash, tracked.Salt))
                throw new UnauthorizedAccessException("The current password is incorrect.");
            var (hash, salt) = _hasher.HashPassword(newPassword);
            tracked.SetPassword(hash, salt);
            tracked.SecurityVersion = AccountSessionRules.NextSecurityVersion(tracked.SecurityVersion);
            LocalAuditTrail.Record(context, tracked, "user.password-changed", "User", tracked.Id);
            await SaveAccountChangesAsync(context);
            await transaction.CommitAsync();
            InvalidateMatchingSession(user.Id, user.SecurityVersion);
        }

        public async Task<List<User>> GetSuperviseesAsync(int supervisorId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await RequireSessionAsync(context);
            if (actor.Id != supervisorId || !actor.HasSupervisorPermissions)
                throw new UnauthorizedAccessException("Only the signed-in supervisor may read this team.");
            return await LoadSafeUsersAsync(context, context.Users.AsNoTracking()
                .Where(u => u.AgencyId == actor.AgencyId && u.SupervisorId == supervisorId &&
                    u.Role != UserRole.PlatformOperator && (u.Permissions & UserPermissions.CaseManagement) != 0));
        }

        public Task SetEnabledAsync(AgencyActor actor, User user, bool isEnabled) =>
            ChangeAccessAsync(actor, user, isEnabled);

        public Task RevokeSessionsAsync(AgencyActor actor, User user) =>
            ChangeAccessAsync(actor, user, requestedEnabled: null);

        private async Task ChangeAccessAsync(AgencyActor suppliedActor, User user, bool? requestedEnabled)
        {
            ArgumentNullException.ThrowIfNull(user);
            await using var context = _contextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var actor = await ValidateActorAsync(context, suppliedActor);
            var target = await context.Users.SingleOrDefaultAsync(item => item.Id == user.Id)
                ?? throw new InvalidOperationException("The user no longer exists.");
            Refuse(AccountSessionRules.DescribeManagementRefusal(actor.ToAgencyActor(), target.Id,
                target.AgencyId, target.Role.ToString(), requestedEnabled));
            if (requestedEnabled == target.IsEnabled)
                return;
            if (target.SecurityVersion != user.SecurityVersion)
                throw new InvalidOperationException("This account changed after it was opened. Reload it and try again.");
            if (requestedEnabled is bool enabled)
                target.IsEnabled = enabled;
            target.SecurityVersion = AccountSessionRules.NextSecurityVersion(target.SecurityVersion);
            var action = requestedEnabled is null ? "user.sessions-revoked" : target.IsEnabled ? "user.enabled" : "user.disabled";
            LocalAuditTrail.Record(context, actor, action, "User", target.Id);
            await SaveAccountChangesAsync(context);
            await transaction.CommitAsync();
            InvalidateMatchingSession(user.Id, user.SecurityVersion);
            // Updating a management row is safe; updating the current session's
            // security stamp would silently mint a replacement sign-in.
            if (!ReferenceEquals(_sessionService?.CurrentUser, user))
            {
                user.IsEnabled = target.IsEnabled;
                user.SecurityVersion = target.SecurityVersion;
            }
        }

        private async Task<User> RequireSessionAsync(SatiContext context) =>
            await LocalTenantAccess.EnsureSessionAsync(context, _sessionService
                ?? throw new UnauthorizedAccessException("A signed-in session is required."));

        private void EnsureLiveActor(User stored, AgencyActor captured)
        {
            if (AccountSessionRules.IsCurrentSession(stored.IsEnabled, stored.SecurityVersion, captured.SecurityVersion))
                return;
            InvalidateMatchingSession(captured.UserId, captured.SecurityVersion);
            throw new SessionExpiredException(new UnauthorizedAccessException(AccountSessionRules.SessionExpired));
        }

        private void InvalidateMatchingSession(int userId, long securityVersion)
        {
            if (_sessionService?.CurrentUser is User current && current.Id == userId && current.SecurityVersion == securityVersion)
                _sessionService.Invalidate(current);
        }

        private static async Task SaveAccountChangesAsync(SatiContext context)
        {
            try { await context.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new InvalidOperationException("This account changed during the request. Reload it and try again.", exception);
            }
        }

        private static async Task<List<User>> LoadSafeUsersAsync(SatiContext context, IQueryable<User> query)
        {
            var profiles = await query.Select(user => new
            {
                user.Id, user.Username, user.DisplayName, user.Role, user.SupervisorId, user.AgencyId,
                user.Permissions, user.Email, user.Phone, user.IsEnabled, user.SecurityVersion
            }).ToListAsync();
            return profiles.Select(profile =>
            {
                var user = User.Create(profile.Id, profile.Username, profile.DisplayName, string.Empty, string.Empty,
                    profile.Role, profile.SupervisorId, profile.AgencyId);
                user.Permissions = profile.Permissions;
                user.Email = profile.Email;
                user.Phone = profile.Phone;
                user.IsEnabled = profile.IsEnabled;
                user.SecurityVersion = profile.SecurityVersion;
                return user;
            }).ToList();
        }
    }
}
