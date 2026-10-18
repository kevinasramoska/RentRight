using Microsoft.Data.SqlClient;

namespace RentRight.Data;

public static class LocalDatabaseInitializer
{
    public static void ValidateTarget(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("LocalDevelopment:AllowInitialization"))
            throw new InvalidOperationException("Local initialization requires Development and explicit local initialization permission.");

        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Local initialization requires a valid Compose database configuration.");
        }

        // Do not let a local helper accidentally migrate the normal developer/shared database.
        if (!string.Equals(connection.DataSource, "sql,1433", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.InitialCatalog, "RentRightLocal", StringComparison.Ordinal))
            throw new InvalidOperationException("Local initialization is restricted to the Compose sql service and RentRightLocal database.");
    }
}
