using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sync.Sharp.Extensions;

namespace Sync.Sharp;

/// <summary>
/// Provides a preconfigured <see cref="IHost"/> builder and runner for standalone or containerized Sync.Sharp worker deployments.
/// </summary>
public static class SyncSharpHost
{
    /// <summary>
    /// Creates a preconfigured <see cref="HostApplicationBuilder"/> with Sync.Sharp configuration, logging, and services registered.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[]? args = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args ?? []
        });

        builder.Configuration.AddSyncSharpConfiguration();

        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });

        builder.Services.AddSyncSharp(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Builds and runs the Sync.Sharp background daemon host until shutdown is triggered.
    /// </summary>
    public static async Task<int> RunAsync(string[]? args = null, CancellationToken cancellationToken = default)
    {
        var builder = CreateBuilder(args);
        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
