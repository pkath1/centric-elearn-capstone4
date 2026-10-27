using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Larchmont.Mcp.Tools;

/// <summary>
/// The guarded free-form tool. The guard (SqlGuard) runs first. The database grants are the first line of defence,
/// and this guard is the second. Anything the guard refuses is audited and never reaches the database.
/// </summary>
[McpServerToolType]
public sealed class ReadonlyQueryTool
{
    private const string ToolName = "run_readonly_query";

    private readonly QueryRunner _runner;

    public ReadonlyQueryTool(QueryRunner runner)
    {
        _runner = runner;
    }

    [McpServerTool(Name = ToolName)]
    [Description("Runs ONE read-only SELECT (or WITH ... SELECT) over the mcp views (v_mcp_*). Base tables, other schemas, " +
                 "comments, multiple statements, user variables, INTO OUTFILE and personal-data columns are refused. " +
                 "Returns at most 200 rows, times out after 5 seconds, and is scoped to the configured hotel. " +
                 "The view list is in docs/mcp-security.md.")]
    public async Task<string> RunReadonlyQueryAsync(
        [Description("A single SELECT statement. Example: SELECT room_number, room_status FROM v_mcp_rooms WHERE room_status = 'Dirty' ORDER BY room_number")] string sql,
        CancellationToken cancellationToken = default)
    {
        var guard = SqlGuard.Check(sql);
        if (!guard.Allowed)
        {
            await _runner.RecordRejectedAsync(ToolName, sql ?? string.Empty, guard.Reason, cancellationToken);
            return HousekeepingTools.Serialize(new { status = "rejected", reason = guard.Reason });
        }

        var result = await _runner.ExecuteAsync(ToolName, sql!, new Dictionary<string, object?>(), cancellationToken);
        return HousekeepingTools.Serialize(new
        {
            status = "ok",
            columns = result.Columns,
            rowCount = result.Rows.Count,
            rowCap = McpConfig.RowCap,
            result.Truncated,
            rows = result.Rows,
        });
    }
}
