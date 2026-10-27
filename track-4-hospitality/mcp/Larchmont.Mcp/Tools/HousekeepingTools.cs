using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Larchmont.Mcp.Tools;

/// <summary>
/// The four business tools. Each runs fixed, parameterised SQL over the mcp views. Clients never supply SQL here.
/// The hotel scope is fixed in the view definitions (db-setup/02-mcp-views.sql), not in these methods.
/// </summary>
[McpServerToolType]
public sealed class HousekeepingTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly QueryRunner _runner;

    public HousekeepingTools(QueryRunner runner)
    {
        _runner = runner;
    }

    [McpServerTool(Name = "rooms_breaching_sla")]
    [Description("Lists rooms whose housekeeping turnaround (checkout to Ready) is still open and past its room-type target " +
                 "(45 minutes, 75 for suites and family rooms). Worst overrun first. Read-only, one hotel only.")]
    public async Task<string> RoomsBreachingSlaAsync(
        [Description("Maximum number of rooms to return, from 1 to 200. Default 50.")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var capped = Math.Clamp(limit, 1, McpConfig.RowCap);
        const string sql = """
            SELECT room_number, floor, room_type_code, room_status, checkout_at, target_minutes,
                   minutes_open, minutes_open - target_minutes AS overrun_minutes
            FROM v_mcp_room_turnarounds
            WHERE is_open = 1 AND sla_status = 'Breach'
            ORDER BY overrun_minutes DESC, room_number
            LIMIT @limit
            """;

        var result = await _runner.ExecuteAsync(
            "rooms_breaching_sla", sql, new Dictionary<string, object?> { ["@limit"] = capped }, cancellationToken);
        return Serialize(new { hotelScope = "configured hotel", count = result.Rows.Count, result.Truncated, rooms = result.Rows });
    }

    [McpServerTool(Name = "ooo_impact_on_occupancy")]
    [Description("Shows how many rooms are out of order now, what share of inventory that is, and how many bookings " +
                 "overlapping a date window sit on out-of-order rooms. Counts bookings, not individual nights. Read-only, one hotel only.")]
    public async Task<string> OooImpactOnOccupancyAsync(
        [Description("First night of the window, as yyyy-MM-dd (inclusive).")] string fromDate,
        [Description("Check-out date of the window, as yyyy-MM-dd (exclusive). Must be after fromDate and at most 92 days later.")] string toDate,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            return Serialize(new { error = "Dates must be in yyyy-MM-dd format." });
        }

        if (to <= from || to.DayNumber - from.DayNumber > 92)
        {
            return Serialize(new { error = "toDate must be after fromDate, and the window can be at most 92 days." });
        }

        const string inventorySql = """
            SELECT CAST(SUM(rooms_total) AS SIGNED) AS rooms_total,
                   CAST(SUM(rooms_out_of_order) AS SIGNED) AS rooms_out_of_order,
                   CAST(SUM(rooms_available) AS SIGNED) AS rooms_available
            FROM v_mcp_room_inventory
            """;

        const string staysSql = """
            SELECT COUNT(*) AS stays_overlapping,
                   CAST(SUM(CASE WHEN room_status = 'OutOfOrder' THEN 1 ELSE 0 END) AS SIGNED) AS stays_on_out_of_order_rooms
            FROM v_mcp_stays
            WHERE check_in < @to_date
              AND check_out > @from_date
              AND booking_status IN ('Confirmed', 'CheckedIn', 'CheckedOut')
            """;

        var inventory = await _runner.ExecuteAsync("ooo_impact_on_occupancy", inventorySql,
            new Dictionary<string, object?>(), cancellationToken);
        var stays = await _runner.ExecuteAsync("ooo_impact_on_occupancy", staysSql,
            new Dictionary<string, object?> { ["@from_date"] = from.ToDateTime(TimeOnly.MinValue), ["@to_date"] = to.ToDateTime(TimeOnly.MinValue) },
            cancellationToken);

        var inv = inventory.Rows.FirstOrDefault() ?? new Dictionary<string, object?>();
        var st = stays.Rows.FirstOrDefault() ?? new Dictionary<string, object?>();
        var total = ToInt(inv.GetValueOrDefault("rooms_total"));
        var outOfOrder = ToInt(inv.GetValueOrDefault("rooms_out_of_order"));

        return Serialize(new
        {
            window = new { from = fromDate, to = toDate },
            rooms = new
            {
                total,
                outOfOrder,
                available = ToInt(inv.GetValueOrDefault("rooms_available")),
                outOfOrderSharePercent = total == 0 ? 0m : Math.Round(outOfOrder * 100m / total, 1),
            },
            bookings = new
            {
                overlapping = ToInt(st.GetValueOrDefault("stays_overlapping")),
                onOutOfOrderRooms = ToInt(st.GetValueOrDefault("stays_on_out_of_order_rooms")),
            },
            notes = new[]
            {
                "Booking counts, not night counts. Out-of-order status is the current status of the room, not its status on each night.",
                "Out-of-order rooms are not counted as available (BR-20).",
            },
        });
    }

    [McpServerTool(Name = "housekeeper_load")]
    [Description("Rooms per housekeeper per floor, with counts by status (dirty, cleaning, inspected, ready, out of order). " +
                 "Housekeepers are shown by user id only. A null housekeeper means the floor has no active assignment. Read-only, one hotel only.")]
    public async Task<string> HousekeeperLoadAsync(
        [Description("Optional floor number. Leave out for all floors.")] int? floor = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT floor, housekeeper_user_id, rooms_total, rooms_dirty, rooms_cleaning,
                   rooms_inspected, rooms_ready, rooms_out_of_order
            FROM v_mcp_housekeeper_load
            WHERE (@floor IS NULL OR floor = @floor)
            ORDER BY floor, housekeeper_user_id
            """;

        var result = await _runner.ExecuteAsync(
            "housekeeper_load",
            sql,
            new Dictionary<string, object?> { ["@floor"] = floor },
            cancellationToken);
        return Serialize(new { count = result.Rows.Count, result.Truncated, floors = result.Rows });
    }

    [McpServerTool(Name = "arrivals_without_ready_room")]
    [Description("Confirmed bookings arriving today whose assigned room is not Ready, or that have no room yet. " +
                 "Shows booking references and room numbers only, no guest details. Read-only, one hotel only.")]
    public async Task<string> ArrivalsWithoutReadyRoomAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT booking_reference, room_number, room_status, check_in, check_out
            FROM v_mcp_arrivals_today
            WHERE room_id IS NULL OR room_status IS NULL OR room_status <> 'Ready'
            ORDER BY room_number, booking_reference
            LIMIT @limit
            """;

        var result = await _runner.ExecuteAsync(
            "arrivals_without_ready_room",
            sql,
            new Dictionary<string, object?> { ["@limit"] = McpConfig.RowCap },
            cancellationToken);
        return Serialize(new { count = result.Rows.Count, result.Truncated, arrivals = result.Rows });
    }

    private static int ToInt(object? value) => value switch
    {
        null => 0,
        int i => i,
        long l => (int)l,
        decimal d => (int)d,
        _ => Convert.ToInt32(value, CultureInfo.InvariantCulture),
    };

    internal static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
