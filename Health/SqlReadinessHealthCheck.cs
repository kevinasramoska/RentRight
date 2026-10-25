using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RentRight.Data;

namespace RentRight.Health;

public sealed class SqlReadinessHealthCheck(ApplicationDbContext database) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the application's configured target, but not its pool, retry policy,
            // transactions or EF execution strategy. A probe must never migrate or write.
            var configured = database.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(configured)) return HealthCheckResult.Unhealthy();
            var connectionString = new SqlConnectionStringBuilder(configured)
            {
                ConnectTimeout = 2, ConnectRetryCount = 0, Pooling = false
            };
            if (string.IsNullOrWhiteSpace(connectionString.DataSource) ||
                string.IsNullOrWhiteSpace(connectionString.InitialCatalog)) return HealthCheckResult.Unhealthy();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Timeout);
            await using var connection = new SqlConnection(connectionString.ConnectionString);
            await connection.OpenAsync(deadline.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 2;
            var result = await command.ExecuteScalarAsync(deadline.Token);
            return result is int value && value == 1 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            // Health reports are also logged by the framework. Do not retain SQL,
            // connection strings or provider exceptions in a result or description.
            return HealthCheckResult.Unhealthy();
        }
    }
}
