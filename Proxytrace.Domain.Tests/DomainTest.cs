using Microsoft.Testing.Platform.Services;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

public class DomainTest<TModule> : BaseTest<TModule>
    where TModule : Autofac.Module, new()
{
    protected async Task<TDomainEntity> GetOrCreate<TDomainEntity>(IServiceProvider services) 
        where TDomainEntity : IDomainEntity
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<TDomainEntity>>();
        return await generator.GetOrCreateAsync(CancellationToken);
    }
}