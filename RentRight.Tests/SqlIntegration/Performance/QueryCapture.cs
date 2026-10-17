using System.Data.Common;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RentRight.Tests.SqlIntegration.Performance;

// Parameters exist only in memory for replay. Artifacts contain SQL templates and allow-listed plan facts.
internal sealed class QueryCapture : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public List<CapturedCommand> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Enabled)
            Commands.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>()
                .Select(p => (SqlParameter)((ICloneable)p).Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}

internal sealed record CapturedCommand(string Sql, SqlParameter[] Parameters)
{
    public async Task<CommandEvidence> ExplainAsync(SqlConnection connection)
    {
        var reads = new List<string>();
        void OnInfo(object sender, SqlInfoMessageEventArgs args)
        {
            foreach (SqlError error in args.Errors)
                if (error.Message.Contains("logical reads", StringComparison.Ordinal)) reads.Add(error.Message);
        }
        connection.InfoMessage += OnInfo;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON;\n" + Sql;
            command.Parameters.AddRange(Parameters.Select(p => (SqlParameter)((ICloneable)p).Clone()).ToArray());
            var plans = new List<PlanOperator>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                do
                {
                    var isPlan = reader.FieldCount == 1 && reader.GetName(0).StartsWith("Microsoft SQL Server", StringComparison.Ordinal);
                    while (await reader.ReadAsync())
                    {
                        if (!isPlan) continue;
                        var xml = XDocument.Parse(reader.GetString(0));
                        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                        foreach (var op in xml.Descendants(ns + "RelOp"))
                        {
                            var objects = op.Elements().SelectMany(e => e.Elements(ns + "Object"));
                            plans.Add(new((string?)op.Attribute("PhysicalOp"), (string?)op.Attribute("LogicalOp"),
                                objects.Select(o => (string?)o.Attribute("Index") ?? (string?)o.Attribute("Table") ?? "").ToArray(),
                                op.Element(ns + "RunTimeInformation")?.Elements(ns + "RunTimeCountersPerThread")
                                    .Sum(c => (double?)c.Attribute("ActualRows") ?? 0)));
                        }
                    }
                } while (await reader.NextResultAsync());
            }
            return new(Sql, reads.Sum(line => Regex.Matches(line, @"(?<!lob )logical reads (\d+)")
                .Sum(match => int.Parse(match.Groups[1].Value))), reads.ToArray(), plans);
        }
        finally
        {
            connection.InfoMessage -= OnInfo;
            await using var reset = connection.CreateCommand();
            reset.CommandText = "SET STATISTICS IO OFF; SET STATISTICS XML OFF;";
            await reset.ExecuteNonQueryAsync();
        }
    }
}

internal sealed record PlanOperator(string? Physical, string? Logical, string[] Objects, double? ActualRows);
internal sealed record CommandEvidence(string Sql, int LogicalReads, string[] StatisticsIo, List<PlanOperator> ActualPlan);
