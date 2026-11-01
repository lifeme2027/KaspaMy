// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// stdio MCP server. stdout carries JSON-RPC only; every log line goes to stderr (redacted).
using KaspaMy.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// R2: invalid limit configuration is a startup error, never a silent fallback to the defaults.
var limitErrors = KaspaMy.Mcp.WalletTools.ValidateLimitEnv();
if (limitErrors.Count > 0)
{
    foreach (var e in limitErrors) Console.Error.WriteLine("kaspamy-mcp: configuration error: " + e);
    Console.Error.WriteLine("kaspamy-mcp: refusing to start.");
    return 78; // EX_CONFIG
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
// F7: the SDK logs full stack traces for failed invocations; keep only critical host errors (still stderr).
builder.Logging.SetMinimumLevel(LogLevel.Critical);
WalletLog.Verbose = Environment.GetEnvironmentVariable("KASPAMY_VERBOSE") == "1";
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "kaspamy", Version = "0.1.0" })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
return 0;
