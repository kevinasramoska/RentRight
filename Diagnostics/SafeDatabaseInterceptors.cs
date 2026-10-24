using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RentRight.Diagnostics;

public sealed class SafeDatabaseCommandInterceptor(RentalDiagnostics diagnostics) : DbCommandInterceptor
{
    private void Record(CommandEndEventData data, string outcome) => diagnostics.RecordDatabase(data.ExecuteMethod switch
    {
        DbCommandMethod.ExecuteReader => "reader", DbCommandMethod.ExecuteScalar => "scalar",
        DbCommandMethod.ExecuteNonQuery => "non_query", _ => "other"
    }, data.StartTime, data.Duration, outcome);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Record(eventData, "completed"); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { Record(eventData, "completed"); return ValueTask.FromResult(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { Record(eventData, "completed"); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    { Record(eventData, "completed"); return ValueTask.FromResult(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { Record(eventData, "completed"); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Record(eventData, "completed"); return ValueTask.FromResult(result); }
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Record(eventData, "failed");
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { Record(eventData, "failed"); return Task.CompletedTask; }
    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) => Record(eventData, "cancelled");
    public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    { Record(eventData, "cancelled"); return Task.CompletedTask; }
}

public sealed class SafeDatabaseConnectionInterceptor(RentalDiagnostics diagnostics) : DbConnectionInterceptor
{
    private void Record(ConnectionEndEventData data, string outcome) =>
        diagnostics.RecordDatabase("connect", data.StartTime, data.Duration, outcome);
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Record(eventData, "completed");
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { Record(eventData, "completed"); return Task.CompletedTask; }
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => Record(eventData, "failed");
    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    { Record(eventData, "failed"); return Task.CompletedTask; }
    public override void ConnectionCanceled(DbConnection connection, ConnectionEndEventData eventData) => Record(eventData, "cancelled");
    public override Task ConnectionCanceledAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { Record(eventData, "cancelled"); return Task.CompletedTask; }
}
