using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserManagementApi.Tests;

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Each test starts its own isolated host/store with a new, ephemeral credential.
    public string Token { get; } = Guid.NewGuid().ToString("N");
    public CapturedLogs Logs { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Program reads this setting before Build(). Host configuration makes it
        // available to the minimal-host entry point from the beginning.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["DemoAuth:Token"] = Token }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(Logs);
        });
    }

    public HttpClient AuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
}

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturedLogger(categoryName, Entries);
    public void Dispose() { }

    private sealed class CapturedLogger(
        string category,
        ConcurrentQueue<(string Category, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, formatter(state, exception)));
    }
}
