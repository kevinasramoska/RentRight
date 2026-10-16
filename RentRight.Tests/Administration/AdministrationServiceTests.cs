using Microsoft.EntityFrameworkCore;
using RentRight.Application.Abstractions;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using Xunit;

namespace RentRight.Tests.Administration;

public class AdministrationServiceTests
{
    [Theory]
    [InlineData(false, false, ListAdministrationUsersStatus.Unauthenticated)]
    [InlineData(true, false, ListAdministrationUsersStatus.Forbidden)]
    public async Task ListUsersAsync_UserWithoutAdminAccess_ReturnsAuthorizationOutcomeWithoutQuerying(
        bool isAuthenticated,
        bool isAdministrator,
        ListAdministrationUsersStatus expectedStatus)
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        var service = CreateService(store, isAuthenticated, isAdministrator);

        var outcome = await service.ListUsersAsync(
            new AdministrationUsersQuery(),
            CancellationToken.None);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(0, store.GetUsersPageCount);
    }

    [Theory]
    [InlineData(false, false, UpdateUserRolesStatus.Unauthenticated)]
    [InlineData(true, false, UpdateUserRolesStatus.Forbidden)]
    public async Task UpdateUserRolesAsync_UserWithoutAdminAccess_ReturnsAuthorizationOutcomeWithoutTransaction(
        bool isAuthenticated,
        bool isAdministrator,
        UpdateUserRolesStatus expectedStatus)
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        var service = CreateService(store, isAuthenticated, isAdministrator);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["Tenant"]),
            CancellationToken.None);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(0, store.SerializableTransactionCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_InvalidRole_ReturnsValidationOutcomeWithoutMutation()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["Tenant", "SuperAdmin"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.InvalidRoles, outcome.Status);
        Assert.Equal(["SuperAdmin"], outcome.Errors);
        Assert.Equal(["Tenant"], store.RolesFor("user-1"));
        Assert.Empty(store.AddCalls);
        Assert.Empty(store.RemoveCalls);
        Assert.Equal(1, store.RollbackCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_DifferentCasing_UsesStoredCanonicalRoleNames()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Admin"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["admin", "TENANT"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.Success, outcome.Status);
        Assert.Equal(["Tenant"], Assert.Single(store.AddCalls));
        Assert.Empty(store.RemoveCalls);
        Assert.Equal(["Admin", "Tenant"], store.RolesFor("user-1"));
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_AddFails_SurfacesIdentityErrorsAndRollsBack()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Admin"]));
        store.AddFailure = AdministrationIdentityOperation.Failure("Role assignment failed.");
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["Admin", "Tenant"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.IdentityFailure, outcome.Status);
        Assert.Equal(["Role assignment failed."], outcome.Errors);
        Assert.Equal(["Admin"], store.RolesFor("user-1"));
        Assert.Empty(store.RemoveCalls);
        Assert.Equal(1, store.RollbackCount);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_RemoveFailsAfterAdd_RollsBackPartialChanges()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        store.RemoveFailure = AdministrationIdentityOperation.Failure("Role removal failed.");
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["Admin"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.IdentityFailure, outcome.Status);
        Assert.Equal(["Admin"], Assert.Single(store.AddCalls));
        Assert.Equal(["Tenant"], Assert.Single(store.RemoveCalls));
        Assert.Equal(["Tenant"], store.RolesFor("user-1"));
        Assert.Equal(1, store.RollbackCount);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_RemovingSoleAdmin_IsRejectedInsideSerializableTransaction()
    {
        var store = CreateStore(("admin-1", "admin@example.com", ["Admin"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("admin-1", ["Tenant"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.LastAdministrator, outcome.Status);
        Assert.Equal(["Admin"], store.RolesFor("admin-1"));
        Assert.Equal(1, store.SerializableTransactionCount);
        Assert.Equal(1, store.RollbackCount);
        Assert.Empty(store.AddCalls);
        Assert.Empty(store.RemoveCalls);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_MultiRoleUser_ChangesOnlyRequestedDifferences()
    {
        var store = CreateStore(
            ("admin-1", "first@example.com", ["Admin", "Landlord"]),
            ("admin-2", "second@example.com", ["Admin"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("admin-1", ["landlord", "tenant"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.Success, outcome.Status);
        Assert.Equal(["Tenant"], Assert.Single(store.AddCalls));
        Assert.Equal(["Admin"], Assert.Single(store.RemoveCalls));
        Assert.Equal(["Landlord", "Tenant"], store.RolesFor("admin-1"));
        Assert.Equal(["Admin"], store.RolesFor("admin-2"));
    }

    [Fact]
    public async Task UpdateUserRolesAsync_MissingUser_RollsBackAndReturnsUserNotFound()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("missing-user", ["Tenant"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.UserNotFound, outcome.Status);
        Assert.Equal(1, store.RollbackCount);
        Assert.Equal(0, store.CommitCount);
        Assert.Empty(store.AddCalls);
        Assert.Empty(store.RemoveCalls);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_UnchangedRoles_CommitsWithoutIdentityMutation()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Landlord", "Tenant"]));
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["tenant", "LANDLORD", "Tenant"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.Success, outcome.Status);
        Assert.Equal("user@example.com", outcome.UserEmail);
        Assert.Empty(store.AddCalls);
        Assert.Empty(store.RemoveCalls);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(0, store.RollbackCount);
    }

    [Fact]
    public async Task UpdateUserRolesAsync_StoreThrowsDbUpdateException_RollsBackAndReturnsConcurrentUpdate()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant"]));
        store.AddException = new DbUpdateException("Concurrent role update.");
        var service = CreateService(store);

        var outcome = await service.UpdateUserRolesAsync(
            new UpdateUserRolesCommand("user-1", ["Tenant", "Landlord"]),
            CancellationToken.None);

        Assert.Equal(UpdateUserRolesStatus.ConcurrentUpdate, outcome.Status);
        Assert.Equal(["Tenant"], store.RolesFor("user-1"));
        Assert.Equal(1, store.RollbackCount);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task ListUsersAsync_ExcessivePageSize_IsBounded()
    {
        var store = CreateStore(("user-1", "user@example.com", ["Tenant", "Landlord"]));
        var service = CreateService(store);

        var outcome = await service.ListUsersAsync(
            new AdministrationUsersQuery(Page: -10, PageSize: 500),
            CancellationToken.None);

        Assert.Equal(ListAdministrationUsersStatus.Success, outcome.Status);
        Assert.Equal(1, store.LastRequestedPage);
        Assert.Equal(AdministrationUsersQuery.MaximumPageSize, store.LastRequestedPageSize);
        Assert.Equal(["Landlord", "Tenant"], Assert.Single(outcome.Page!.Users).Roles);
    }

    private static AdministrationService CreateService(
        FakeAdministrationRoleStore store,
        bool isAuthenticated = true,
        bool isAdministrator = true) =>
        new(store, new FakeCurrentUser(isAuthenticated, isAdministrator));

    private static FakeAdministrationRoleStore CreateStore(
        params (string UserId, string Email, string[] Roles)[] users) =>
        new(["Admin", "Landlord", "Tenant"], users);

    private sealed class FakeCurrentUser(bool isAuthenticated, bool isAdministrator) : ICurrentUser
    {
        public bool IsAuthenticated { get; } = isAuthenticated;
        public string? UserId => "current-admin";
        public bool IsInRole(string roleName) =>
            isAdministrator && string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAdministrationRoleStore : IAdministrationRoleStore
    {
        private readonly IReadOnlyList<string> _storedRoles;
        private readonly Dictionary<string, UserState> _users;

        public FakeAdministrationRoleStore(
            IReadOnlyList<string> storedRoles,
            IEnumerable<(string UserId, string Email, string[] Roles)> users)
        {
            _storedRoles = storedRoles;
            _users = users.ToDictionary(
                user => user.UserId,
                user => new UserState(
                    user.Email,
                    new HashSet<string>(user.Roles, StringComparer.OrdinalIgnoreCase)));
        }

        public AdministrationIdentityOperation? AddFailure { get; set; }
        public AdministrationIdentityOperation? RemoveFailure { get; set; }
        public DbUpdateException? AddException { get; set; }
        public List<IReadOnlyList<string>> AddCalls { get; } = [];
        public List<IReadOnlyList<string>> RemoveCalls { get; } = [];
        public int SerializableTransactionCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public int LastRequestedPage { get; private set; }
        public int LastRequestedPageSize { get; private set; }
        public int GetUsersPageCount { get; private set; }

        public IReadOnlyList<string> RolesFor(string userId) =>
            _users[userId].Roles.OrderBy(role => role).ToArray();

        public Task<AdministrationUsersPage> GetUsersPageAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            GetUsersPageCount++;
            LastRequestedPage = page;
            LastRequestedPageSize = pageSize;
            var rows = _users
                .OrderBy(user => user.Value.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(user => new AdministrationUserRow(
                    user.Key,
                    user.Value.Email,
                    user.Value.Email,
                    user.Value.Roles.OrderBy(role => role).ToArray()))
                .ToArray();

            return Task.FromResult(new AdministrationUsersPage(
                rows,
                _storedRoles,
                page,
                pageSize,
                _users.Count));
        }

        public Task<IReadOnlyList<string>> GetStoredRoleNamesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(_storedRoles);

        public Task<AdministrationUserRoleState?> GetUserRoleStateAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                return Task.FromResult<AdministrationUserRoleState?>(null);
            }

            return Task.FromResult<AdministrationUserRoleState?>(new(
                userId,
                user.Email,
                user.Roles.ToArray()));
        }

        public Task<int> CountUsersInRoleAsync(
            string roleName,
            CancellationToken cancellationToken) =>
            Task.FromResult(_users.Count(user => user.Value.Roles.Contains(roleName)));

        public Task<AdministrationIdentityOperation> AddToRolesAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            AddCalls.Add(roles.ToArray());
            if (AddException is not null)
            {
                throw AddException;
            }

            if (AddFailure is not null)
            {
                return Task.FromResult(AddFailure);
            }

            _users[userId].Roles.UnionWith(roles);
            return Task.FromResult(AdministrationIdentityOperation.Success());
        }

        public Task<AdministrationIdentityOperation> RemoveFromRolesAsync(
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            RemoveCalls.Add(roles.ToArray());
            _users[userId].Roles.ExceptWith(roles);
            return Task.FromResult(RemoveFailure ?? AdministrationIdentityOperation.Success());
        }

        public Task<IAdministrationRoleTransaction> BeginSerializableTransactionAsync(
            CancellationToken cancellationToken)
        {
            SerializableTransactionCount++;
            var snapshot = _users.ToDictionary(
                user => user.Key,
                user => new UserState(
                    user.Value.Email,
                    new HashSet<string>(user.Value.Roles, StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult<IAdministrationRoleTransaction>(
                new FakeTransaction(this, snapshot));
        }

        private void Restore(Dictionary<string, UserState> snapshot)
        {
            _users.Clear();
            foreach (var user in snapshot)
            {
                _users[user.Key] = user.Value;
            }
        }

        private sealed record UserState(string Email, HashSet<string> Roles);

        private sealed class FakeTransaction(
            FakeAdministrationRoleStore store,
            Dictionary<string, UserState> snapshot) : IAdministrationRoleTransaction
        {
            private bool _completed;

            public Task CommitAsync(CancellationToken cancellationToken)
            {
                _completed = true;
                store.CommitCount++;
                return Task.CompletedTask;
            }

            public Task RollbackAsync(CancellationToken cancellationToken)
            {
                if (!_completed)
                {
                    store.Restore(snapshot);
                    store.RollbackCount++;
                    _completed = true;
                }

                return Task.CompletedTask;
            }

            public async ValueTask DisposeAsync()
            {
                if (!_completed)
                {
                    await RollbackAsync(CancellationToken.None);
                }
            }
        }
    }
}
