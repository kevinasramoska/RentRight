using Testcontainers.MsSql;
using Xunit;

namespace RentRight.Tests.SqlIntegration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword($"T!{Guid.NewGuid():N}a9")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }

            throw new InvalidOperationException(
                "SQL integration tests require a running Docker daemon with Linux SQL Server container support. Run docker info before retrying; no developer database fallback is used.",
                exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
