using Larchmont.Mcp;
using Xunit;

namespace Larchmont.Mcp.Tests;

/// <summary>Negative and positive tests for SqlGuard. No database is needed.</summary>
public class SqlGuardTests
{
    [Theory]
    [InlineData("UPDATE v_mcp_rooms SET room_number = '1'")]
    [InlineData("DELETE FROM v_mcp_rooms")]
    [InlineData("INSERT INTO mcp_query_audit (tool) VALUES ('x')")]
    [InlineData("REPLACE INTO v_mcp_rooms VALUES (1)")]
    [InlineData("SET @x = 1")]
    [InlineData("CALL some_procedure()")]
    [InlineData("GRANT ALL PRIVILEGES ON *.* TO 'x'@'%'")]
    [InlineData("LOAD DATA INFILE '/tmp/x' INTO TABLE v_mcp_rooms")]
    [InlineData("SHOW GRANTS FOR CURRENT_USER")]
    [InlineData("EXPLAIN SELECT * FROM v_mcp_rooms")]
    public void Given_WriteOrAdminStatement_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("DROP TABLE v_mcp_rooms")]
    [InlineData("DROP VIEW v_mcp_rooms")]
    [InlineData("CREATE TABLE evil (id INT)")]
    [InlineData("ALTER TABLE v_mcp_rooms ADD COLUMN x INT")]
    [InlineData("TRUNCATE mcp_query_audit")]
    [InlineData("RENAME TABLE v_mcp_rooms TO x")]
    public void Given_DdlStatement_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE v_mcp_rooms")]
    [InlineData("SELECT * FROM v_mcp_rooms;SELECT * FROM v_mcp_stays")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1; SELECT 2;")]
    public void Given_MultipleStatements_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT * FROM v_mcp_rooms -- ; DELETE FROM v_mcp_rooms")]
    [InlineData("SELECT * FROM v_mcp_rooms # ; DELETE FROM v_mcp_rooms")]
    [InlineData("SELECT 1 /* ; DELETE FROM v_mcp_rooms */")]
    [InlineData("SELECT 1 /*! DELETE FROM v_mcp_rooms */")]
    [InlineData("SELECT * FROM v_mcp_rooms WHERE room_number = 'x' --' ; DROP TABLE rooms")]
    public void Given_CommentHidingSecondStatement_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT email FROM v_mcp_stays")]
    [InlineData("SELECT first_name, last_name FROM v_mcp_arrivals_today")]
    [InlineData("SELECT password_hash FROM v_mcp_rooms")]
    [InlineData("SELECT room_number AS phone FROM v_mcp_rooms")]
    [InlineData("SELECT display_name FROM v_mcp_housekeeper_load")]
    [InlineData("SELECT notes FROM v_mcp_rooms")]
    [InlineData("SELECT ooo_reason FROM v_mcp_out_of_order_rooms")]
    [InlineData("SELECT r.email FROM v_mcp_rooms AS r")]
    public void Given_PersonalDataColumn_When_Checked_Then_Rejected(string sql)
    {
        var result = SqlGuard.Check(sql);
        Assert.False(result.Allowed);
        Assert.Contains("personal data", result.Reason);
    }

    [Theory]
    [InlineData("SELECT * FROM rooms")]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT * FROM guests")]
    [InlineData("SELECT * FROM bookings")]
    [InlineData("SELECT * FROM room_turnarounds")]
    [InlineData("SELECT * FROM v_mcp_rooms, rooms")]
    [InlineData("SELECT * FROM v_mcp_rooms UNION SELECT * FROM users")]
    [InlineData("SELECT * FROM (SELECT * FROM rooms) AS t")]
    [InlineData("SELECT * FROM v_mcp_rooms WHERE room_id IN (SELECT id FROM guests)")]
    [InlineData("SELECT * FROM v_mcp_rooms JOIN users ON users.id = 1")]
    public void Given_BaseTable_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT * FROM mysql.user")]
    [InlineData("SELECT * FROM performance_schema.threads")]
    [InlineData("SELECT * FROM larchmont.rooms")]
    [InlineData("SELECT * FROM larchmont_scratch.v_mcp_rooms")]
    [InlineData("SELECT * FROM otherdb.v_mcp_rooms")]
    public void Given_OtherSchema_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT room_number FROM v_mcp_rooms INTO OUTFILE '/tmp/rooms.csv'")]
    [InlineData("SELECT room_number FROM v_mcp_rooms INTO DUMPFILE '/tmp/rooms.bin'")]
    [InlineData("SELECT room_number INTO @x FROM v_mcp_rooms")]
    [InlineData("SELECT LOAD_FILE('/etc/passwd')")]
    [InlineData("SELECT SLEEP(10)")]
    [InlineData("SELECT * FROM v_mcp_rooms WHERE SLEEP(5) = 0")]
    [InlineData("SELECT BENCHMARK(1000000, MD5('a'))")]
    [InlineData("SELECT GET_LOCK('x', 10)")]
    [InlineData("SELECT * FROM v_mcp_rooms FOR UPDATE")]
    [InlineData("SELECT * FROM v_mcp_rooms LOCK IN SHARE MODE")]
    public void Given_ServerSideFileOrDelayFunction_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT @@version")]
    [InlineData("SELECT @x := 1")]
    [InlineData("SELECT `email` FROM v_mcp_stays")]
    [InlineData("SELECT 'unterminated FROM v_mcp_rooms")]
    [InlineData("SELECT 1 \\ 2")]
    [InlineData("SELECT ? FROM v_mcp_rooms")]
    [InlineData("SELECT 1 : 2")]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_LexicallyUnsafeInput_When_Checked_Then_Rejected(string sql)
    {
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Fact]
    public void Given_OverlongStatement_When_Checked_Then_Rejected()
    {
        var sql = "SELECT room_number FROM v_mcp_rooms WHERE room_number = '" + new string('1', SqlGuard.MaxStatementLength) + "'";
        Assert.False(SqlGuard.Check(sql).Allowed);
    }

    [Theory]
    [InlineData("SELECT room_number, room_status FROM v_mcp_rooms WHERE room_status = 'Dirty' ORDER BY room_number LIMIT 10")]
    [InlineData("SELECT room_number, floor FROM v_mcp_rooms;")]
    [InlineData("SELECT * FROM v_mcp_room_turnarounds WHERE is_open = 1 AND sla_status = 'Breach'")]
    [InlineData("SELECT r.room_number, t.target_minutes FROM v_mcp_room_turnarounds AS t JOIN v_mcp_rooms r ON r.room_id = t.room_id")]
    [InlineData("SELECT floor, SUM(rooms_dirty) AS dirty FROM v_mcp_housekeeper_load GROUP BY floor")]
    [InlineData("SELECT CAST(rooms_available AS SIGNED) AS available FROM v_mcp_room_inventory")]
    [InlineData("WITH breaches AS (SELECT room_number, minutes_open FROM v_mcp_room_turnarounds WHERE is_open = 1 AND sla_status = 'Breach') SELECT COUNT(*) AS n FROM breaches")]
    [InlineData("SELECT booking_reference, room_number FROM v_mcp_arrivals_today WHERE room_id IS NULL OR room_status <> 'Ready'")]
    [InlineData("SELECT COUNT(*) FROM v_mcp_stays WHERE check_in < '2026-10-27' AND check_out > '2026-10-20' AND booking_status IN ('Confirmed', 'CheckedIn')")]
    [InlineData("SELECT hotel_id, room_type_code, rooms_out_of_order FROM v_mcp_room_inventory WHERE rooms_out_of_order > 0")]
    [InlineData("SELECT room_number FROM v_mcp_out_of_order_rooms WHERE has_reason = 0 ORDER BY ooo_since")]
    [InlineData("SELECT room_number, CASE WHEN room_status = 'Ready' THEN 'ok' ELSE 'not ready' END AS state FROM v_mcp_rooms")]
    [InlineData("SELECT * FROM v_mcp_rooms WHERE room_id IN (SELECT room_id FROM v_mcp_stays)")]
    public void Given_ValidSelectOnViews_When_Checked_Then_Accepted(string sql)
    {
        var result = SqlGuard.Check(sql);
        Assert.True(result.Allowed, result.Reason);
    }

    [Fact]
    public void Given_SelectOnViewWithNoPersonalColumns_When_Checked_Then_Accepted()
    {
        Assert.True(SqlGuard.Check("SELECT booking_reference, check_in FROM v_mcp_stays").Allowed);
    }

    [Fact]
    public void Given_ViewLists_When_Inspected_Then_NoViewExposesPersonalData()
    {
        foreach (var (view, columns) in McpSchema.Views)
        {
            foreach (var column in columns)
            {
                Assert.False(McpSchema.PersonalColumns.Contains(column), $"{view} exposes personal column {column}");
            }
        }
    }

    [Fact]
    public void Given_ViewNames_When_Inspected_Then_NoneIsABaseTable()
    {
        foreach (var view in McpSchema.Views.Keys)
        {
            Assert.DoesNotContain(view, McpSchema.BaseTables);
            Assert.StartsWith("v_mcp_", view);
        }
    }

    [Fact]
    public void Given_ViewColumns_When_Inspected_Then_NoColumnNamesABaseTable()
    {
        foreach (var column in McpSchema.ViewColumns)
        {
            Assert.DoesNotContain(column, McpSchema.BaseTables);
        }
    }
}
