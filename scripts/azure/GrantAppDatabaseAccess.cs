#:package Microsoft.Data.SqlClient@6.1.1
#:package System.Security.Cryptography.Pkcs@9.0.21
// Grants the web app's user-assigned managed identity read/write access to the demo database.
// Run by .github/workflows/deploy-azure.yml as a member of the SQL Entra admin group, after migrations.
// The user is created from the identity's client ID (WITH SID ... TYPE = E), so the SQL server needs no
// Microsoft Graph permissions. Idempotent; no schema/DDL rights are granted to the app.
// Usage: dotnet run GrantAppDatabaseAccess.cs -- <server-fqdn> <database> <identity-name> <identity-client-id>
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

if (args.Length != 4 || !Guid.TryParse(args[3], out var clientId) ||
    !Regex.IsMatch(args[2], "^[A-Za-z0-9-]{3,128}$"))
{
    Console.Error.WriteLine("Usage: <server-fqdn> <database> <identity-name> <identity-client-id>");
    return 2;
}

var connectionString = new SqlConnectionStringBuilder
{
    DataSource = $"tcp:{args[0]},1433",
    InitialCatalog = args[1],
    Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
    Encrypt = SqlConnectionEncryptOption.Mandatory,
    TrustServerCertificate = false,
    ConnectTimeout = 120 // A paused free-tier database can take about a minute to resume.
}.ConnectionString;

const string grant = """
    DECLARE @sid nvarchar(100) = CONVERT(nvarchar(100), CAST(@clientId AS varbinary(16)), 1);
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
        EXEC (N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @sid + N', TYPE = E;');
    EXEC (N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@name) + N';');
    EXEC (N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@name) + N';');
    """;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await using var command = new SqlCommand(grant, connection);
command.Parameters.Add("@clientId", System.Data.SqlDbType.UniqueIdentifier).Value = clientId;
command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 128).Value = args[2];
await command.ExecuteNonQueryAsync();
Console.WriteLine($"Granted db_datareader and db_datawriter to {args[2]}.");
return 0;
