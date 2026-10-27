using Larchmont.Mcp;
using Xunit;

namespace Larchmont.Mcp.Tests;

/// <summary>Startup checks. The server must fail fast and must never echo a connection string.</summary>
public class McpConfigTests
{
    // No credential here: these tests check names and redaction, not authentication.
    private const string FakeConnection = "Server=db.example.test;Port=3306;Database=larchmont;User ID=larchmont_mcp;";

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    [Fact]
    public void Given_NoConnectionVariable_When_Loaded_Then_FailsAndNamesVariableOnly()
    {
        var ex = Assert.Throws<McpConfigException>(() => McpConfig.Load(Env((McpConfig.HotelVariable, "1"))));
        Assert.Contains(McpConfig.ConnectionVariable, ex.Message);
        Assert.DoesNotContain("example.test", ex.Message);
    }

    [Fact]
    public void Given_NoHotelVariable_When_Loaded_Then_Fails()
    {
        var ex = Assert.Throws<McpConfigException>(() => McpConfig.Load(Env((McpConfig.ConnectionVariable, FakeConnection))));
        Assert.Contains(McpConfig.HotelVariable, ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void Given_BadHotelId_When_Loaded_Then_Fails(string hotelId)
    {
        var ex = Assert.Throws<McpConfigException>(() =>
            McpConfig.Load(Env((McpConfig.ConnectionVariable, FakeConnection), (McpConfig.HotelVariable, hotelId))));
        Assert.Contains(McpConfig.HotelVariable, ex.Message);
        Assert.DoesNotContain(FakeConnection, ex.Message);
    }

    [Fact]
    public void Given_RootAccount_When_Loaded_Then_Refused()
    {
        var ex = Assert.Throws<McpConfigException>(() =>
            McpConfig.Load(Env((McpConfig.ConnectionVariable, "Server=db.example.test;User ID=root;"), (McpConfig.HotelVariable, "1"))));
        Assert.Contains("root", ex.Message);
        Assert.DoesNotContain("example.test", ex.Message);
    }

    [Fact]
    public void Given_ValidSettings_When_Loaded_Then_HotelIdIsRead()
    {
        var config = McpConfig.Load(Env((McpConfig.ConnectionVariable, FakeConnection), (McpConfig.HotelVariable, "3")));
        Assert.Equal(3, config.HotelId);
    }

    [Fact]
    public void Given_ValidSettings_When_Printed_Then_ConnectionStringIsRedacted()
    {
        var config = McpConfig.Load(Env((McpConfig.ConnectionVariable, FakeConnection), (McpConfig.HotelVariable, "3")));
        var text = config.ToString();
        Assert.DoesNotContain("example.test", text);
        Assert.DoesNotContain("larchmont_mcp", text);
        Assert.Contains("redacted", text);
    }
}
