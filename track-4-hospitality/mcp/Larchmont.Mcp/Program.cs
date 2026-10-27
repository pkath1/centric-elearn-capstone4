using Larchmont.Mcp;
using Larchmont.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Startup checks come first. They print variable names only, never values, and exit with code 2 before any protocol traffic.
McpConfig config;
try
{
    config = McpConfig.Load(Environment.GetEnvironmentVariable);
}
catch (McpConfigException ex)
{
    Console.Error.WriteLine($"Larchmont MCP server cannot start: {ex.Message}");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so logging goes to stderr only, and only warnings and above.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<QueryRunner>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<HousekeepingTools>()
    .WithTools<ReadonlyQueryTool>();

await builder.Build().RunAsync();
return 0;
