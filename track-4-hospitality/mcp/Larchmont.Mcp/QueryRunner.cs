using System.Data;
using MySqlConnector;

namespace Larchmont.Mcp;

/// <summary>Rows returned by a query, already capped.</summary>
public sealed record QueryResult(IReadOnlyList<string> Columns, List<Dictionary<string, object?>> Rows, bool Truncated);

/// <summary>Raised with a client-safe message only. Driver details are logged by type and error number, never by text.</summary>
public sealed class McpQueryException(string message) : Exception(message);

/// <summary>
/// Runs statements for the MCP tools. The hotel scope is part of the view definitions (02-mcp-views.sql), not
/// set per connection. Every call writes one row to mcp_query_audit (outcome, row count, time).
/// The connection string is used to open connections and is never written to output or logs.
/// </summary>
public sealed class QueryRunner
{
    private const int StatementTimeoutMilliseconds = McpConfig.TimeoutSeconds * 1000;

    private readonly McpConfig _config;

    public QueryRunner(McpConfig config)
    {
        _config = config;
    }

    /// <summary>Runs a statement that the caller has already checked (fixed SQL in the tools, or a guard-accepted query).</summary>
    public async Task<QueryResult> ExecuteAsync(string tool, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        QueryResult result;
        try
        {
            result = await RunAsync(sql, parameters, ct);
        }
        catch (MySqlException ex)
        {
            var timedOut = ex.Number is 1969 or 3024;
            await TryAuditAsync(tool, sql, null, timedOut ? "timeout" : "error", $"MySQL error {ex.Number}", ct);
            throw new McpQueryException(timedOut
                ? $"The query was stopped after {McpConfig.TimeoutSeconds} seconds."
                : "The database returned an error. The details are in the server log.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await TryAuditAsync(tool, sql, null, "timeout", "command timeout", ct);
            throw new McpQueryException($"The query was stopped after {McpConfig.TimeoutSeconds} seconds.");
        }

        var audited = await TryAuditAsync(tool, sql, result.Rows.Count, "ok", result.Truncated ? "row cap reached" : null, ct);
        if (!audited)
        {
            // Fail closed: no audit row, no data.
            throw new McpQueryException("The call could not be recorded in the audit log, so no results are returned.");
        }

        return result;
    }

    /// <summary>Records a call that the guard refused. Failure to audit is logged by type, and the refusal still stands.</summary>
    public Task RecordRejectedAsync(string tool, string sql, string reason, CancellationToken ct) =>
        TryAuditAsync(tool, sql, null, "rejected", reason, ct);

    private async Task<QueryResult> RunAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_config.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var timeout = new MySqlCommand($"SET SESSION max_execution_time = {StatementTimeoutMilliseconds}", connection))
        {
            await timeout.ExecuteNonQueryAsync(ct);
        }

        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = McpConfig.TimeoutSeconds };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<Dictionary<string, object?>>();
        var truncated = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == McpConfig.RowCap)
            {
                truncated = true;
                command.Cancel();
                break;
            }

            var row = new Dictionary<string, object?>(columns.Count);
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = ReadValue(reader, i);
            }

            rows.Add(row);
        }

        return new QueryResult(columns, rows, truncated);
    }

    private static object? ReadValue(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is DateTime dateTime && string.Equals(reader.GetDataTypeName(ordinal), "DATE", StringComparison.OrdinalIgnoreCase))
        {
            return DateOnly.FromDateTime(dateTime).ToString("yyyy-MM-dd");
        }

        return value;
    }

    /// <summary>Writes one audit row through a fresh connection. Returns false if the row could not be written.</summary>
    private async Task<bool> TryAuditAsync(string tool, string sql, int? rowCount, string outcome, string? detail, CancellationToken ct)
    {
        try
        {
            await using var connection = new MySqlConnection(_config.ConnectionString);
            await connection.OpenAsync(ct);

            const string insert = "INSERT INTO mcp_query_audit " +
                                  "(created_at, tool, db_user, hotel_id, statement_text, row_count, outcome, detail) " +
                                  "VALUES (UTC_TIMESTAMP(3), @tool, USER(), @hotel, @statement, @rows, @outcome, @detail)";
            await using var command = new MySqlCommand(insert, connection);
            command.Parameters.AddWithValue("@tool", Truncate(tool, 40));
            command.Parameters.AddWithValue("@hotel", _config.HotelId);
            command.Parameters.AddWithValue("@statement", Truncate(sql, 4000));
            command.Parameters.AddWithValue("@rows", rowCount.HasValue ? rowCount.Value : DBNull.Value);
            command.Parameters.AddWithValue("@outcome", Truncate(outcome, 20));
            command.Parameters.AddWithValue("@detail", detail is null ? DBNull.Value : Truncate(detail, 255));
            await command.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log the exception type only: driver messages can carry host details.
            Console.Error.WriteLine($"[larchmont-mcp] audit write failed for tool '{tool}' ({ex.GetType().Name}).");
            return false;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
