using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RentRight.Authorization;

namespace RentRight.Tests.Api;

public sealed class RentRightApplicationFactory(
    string environmentName,
    Action<IServiceCollection>? configureServices = null) :
    WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseSetting("SeedAdmin:Email", string.Empty);
        builder.UseSetting("SeedAdmin:Password", string.Empty);
        builder.UseSetting("Geocoding:Enabled", "false"); // Never inherit an operator's live provider settings.
        builder.ConfigureServices(services =>
        {
            // These HTTP contract tests use protected synthetic cookies, not persisted accounts.
            // Real login, stamp/lockout and revocation checks run unchanged in the SQL suite.
            services.RemoveAll<IAccountSessionValidator>();
            services.AddScoped<IAccountSessionValidator, SyntheticSessionValidator>();
            services.RemoveAll<IRoleStore<IdentityRole>>();
            services.AddSingleton<IRoleStore<IdentityRole>, InMemoryRoleStore>();
            services.AddControllers()
                .PartManager.ApplicationParts.Add(
                    new AssemblyPart(typeof(ApiConventionProbeController).Assembly));
            configureServices?.Invoke(services);
        });
    }

    private sealed class SyntheticSessionValidator : IAccountSessionValidator
    {
        public Task ValidateAsync(CookieValidatePrincipalContext context) => Task.CompletedTask;
    }

    private sealed class InMemoryRoleStore : IRoleStore<IdentityRole>
    {
        private readonly Dictionary<string, IdentityRole> _roles =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<IdentityResult> CreateAsync(
            IdentityRole role,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _roles[role.NormalizedName ?? role.Name ?? string.Empty] = role;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityRole?> FindByIdAsync(
            string roleId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_roles.Values.SingleOrDefault(role => role.Id == roleId));
        }

        public Task<IdentityRole?> FindByNameAsync(
            string normalizedRoleName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_roles.GetValueOrDefault(normalizedRoleName));
        }

        public Task<string?> GetNormalizedRoleNameAsync(
            IdentityRole role,
            CancellationToken cancellationToken) =>
            Task.FromResult(role.NormalizedName);

        public Task<string> GetRoleIdAsync(
            IdentityRole role,
            CancellationToken cancellationToken) =>
            Task.FromResult(role.Id);

        public Task<string?> GetRoleNameAsync(
            IdentityRole role,
            CancellationToken cancellationToken) =>
            Task.FromResult(role.Name);

        public Task SetNormalizedRoleNameAsync(
            IdentityRole role,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetRoleNameAsync(
            IdentityRole role,
            string? roleName,
            CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(
            IdentityRole role,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(
            IdentityRole role,
            CancellationToken cancellationToken)
        {
            _roles.Remove(role.NormalizedName ?? role.Name ?? string.Empty);
            return Task.FromResult(IdentityResult.Success);
        }

        public void Dispose()
        {
        }
    }
}
