using Microsoft.Extensions.Hosting;

namespace Nordstein.Core.Common.Hosting;

public class NullHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) 
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) 
        => Task.CompletedTask;
}