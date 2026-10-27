namespace Larchmont.Mcp;

/// <summary>
/// The allowlist the guard checks against. It must match db-setup/02-mcp-views.sql.
/// Views are the only objects a client may name. Columns listed here are the only columns it may use.
/// </summary>
public static class McpSchema
{

    public static readonly IReadOnlyDictionary<string, string[]> Views =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["v_mcp_room_turnarounds"] = ["hotel_id", "room_id", "room_number", "floor", "room_type_code", "room_status",
                "is_open", "checkout_at", "ready_at", "target_minutes", "minutes_open", "sla_status"],
            ["v_mcp_rooms"] = ["hotel_id", "room_id", "room_number", "floor", "room_type_code", "room_status",
                "simple_turn", "ooo_since", "ooo_expected_return"],
            ["v_mcp_out_of_order_rooms"] = ["hotel_id", "room_id", "room_number", "floor", "room_type_code",
                "ooo_since", "ooo_expected_return", "has_reason"],
            ["v_mcp_housekeeper_load"] = ["hotel_id", "floor", "housekeeper_user_id", "rooms_total", "rooms_dirty",
                "rooms_cleaning", "rooms_inspected", "rooms_ready", "rooms_out_of_order"],
            ["v_mcp_arrivals_today"] = ["hotel_id", "booking_reference", "booking_status", "check_in", "check_out",
                "room_id", "room_number", "room_status"],
            ["v_mcp_room_inventory"] = ["hotel_id", "room_type_code", "rooms_total", "rooms_out_of_order", "rooms_available"],
            ["v_mcp_stays"] = ["hotel_id", "booking_reference", "booking_status", "check_in", "check_out",
                "room_id", "room_number", "room_status", "room_type_code"],
        };

    public static readonly HashSet<string> ViewNames =
        new(Views.Keys, StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> ViewColumns =
        new(Views.Values.SelectMany(columns => columns), StringComparer.OrdinalIgnoreCase);

    /// <summary>Base tables. Naming one is refused with a message that points at the views.</summary>
    public static readonly HashSet<string> BaseTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "hotels", "room_types", "rate_plans", "room_rates", "users", "rooms", "room_status_history",
        "guests", "bookings", "booking_notes", "room_turnarounds", "floor_assignments", "room_spot_checks",
        "mcp_query_audit", "__EFMigrationsHistory", "schema_versions", "SchemaVersions",
    };

    /// <summary>
    /// Column names that hold personal data or free text. Refused even if a view someday exposed them.
    /// Staff contact details, guest contact and address details, password hashes, login times, notes.
    /// </summary>
    public static readonly HashSet<string> PersonalColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "first_name", "last_name", "guest_name", "email", "phone", "address_line", "city", "postcode",
        "country", "password_hash", "display_name", "notes", "note", "reason", "ooo_reason",
        "last_login_at", "loyalty_tier", "external_ref",
    };
}
