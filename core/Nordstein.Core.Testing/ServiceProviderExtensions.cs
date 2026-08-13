using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Lifecycle;

namespace Nordstein.Core.Testing;

public static class ServiceProviderExtensions
{
    public static ITempDirectory GetTempDirectory(this IServiceProvider services, string? prefix = null)
    {
        var factory = services.GetRequiredService<ITempDirectory.Create>();
        return factory(prefix: prefix);
    }
}