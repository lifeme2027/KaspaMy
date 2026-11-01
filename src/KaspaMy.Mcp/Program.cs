// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// stdio MCP server. stdout carries JSON-RPC only; every log line goes to stderr (redacted).
using KaspaMy.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
WalletLog.Verbose = Environment.GetEnvironmentVariable("KASPAMY_VERBOSE") == "1";
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "kaspamy", Version = "0.1.0" })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
