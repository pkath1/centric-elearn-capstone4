using MySqlConnector;

namespace Larchmont.Mcp;

/// <summary>
/// Startup settings, read from environment variables only. The connection string is never printed,
/// logged or returned to a client. ToString() is overridden so an accidental log of this object stays safe.
/// </summary>
public sealed class McpConfig
{
    public const string ConnectionVariable = "LARCHMONT_MCP_CONNECTION";
    public const string HotelVariable = "LARCHMONT_MCP_HOTEL_ID";

    public const int RowCap = 200;
    public const int TimeoutSeconds = 5;

    private McpConfig(string connectionString, int hotelId)
    {
        ConnectionString = connectionString;
        HotelId = hotelId;
    }

    public string ConnectionString { get; }

    public int HotelId { get; }

    /// <summary>Reads and checks the settings. Throws <see cref="McpConfigException"/> with a message that names variables, never values.</summary>
    public static McpConfig Load(Func<string, string?> getEnvironmentVariable)
    {
        var connection = getEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new McpConfigException(
                $"Environment variable {ConnectionVariable} is not set. Set it to the connection string for the " +
                "larchmont_mcp database user (see docs/mcp-security.md). The value is never printed.");
        }

        string user;
        try
        {
            var builder = new MySqlConnectionStringBuilder(connection);
            user = builder.UserID ?? string.Empty;
        }
        catch (Exception)
        {
            // Do not include the parser's message: it may echo part of the connection string.
            throw new McpConfigException($"Environment variable {ConnectionVariable} is not a valid MySQL connection string.");
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            throw new McpConfigException($"Environment variable {ConnectionVariable} must name a database user.");
        }

        if (string.Equals(user, "root", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpConfigException($"Environment variable {ConnectionVariable} must not use the root account. Use larchmont_mcp.");
        }

        var hotel = getEnvironmentVariable(HotelVariable);
        if (string.IsNullOrWhiteSpace(hotel))
        {
            throw new McpConfigException(
                $"Environment variable {HotelVariable} is not set. Set it to the numeric id of the one hotel this server may read (hotels.id).");
        }

        if (!int.TryParse(hotel, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var hotelId) || hotelId <= 0)
        {
            throw new McpConfigException($"Environment variable {HotelVariable} must be a positive whole number.");
        }

        return new McpConfig(connection, hotelId);
    }

    public override string ToString() => $"McpConfig(hotelId={HotelId}, connection=<redacted>)";
}

public sealed class McpConfigException(string message) : Exception(message);
