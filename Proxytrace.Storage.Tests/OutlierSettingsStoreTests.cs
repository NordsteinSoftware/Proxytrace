using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Outliers;
using Nordstein.Core.Testing;
using OutlierSettingsEntity = Proxytrace.Storage.Internal.Entities.OutlierSettings.OutlierSettingsEntity;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class OutlierSettingsStoreTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAsync_WhenNothingSaved_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var store = services.GetRequiredService<IOutlierSettingsStore>();

        (await store.GetAsync(CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task SaveThenGet_RoundTripsAllFields()
    {
        IServiceProvider services = GetServices();
        var store = services.GetRequiredService<IOutlierSettingsStore>();
        var settings = new OutlierSettings(Enabled: false, SigmaMultiplier: 2.5, MinSampleCount: 40, SampleWindow: 500);

        await store.SaveAsync(settings, CancellationToken);
        var loaded = await store.GetAsync(CancellationToken);

        loaded.Should().Be(settings);
    }

    [TestMethod]
    public async Task Save_Twice_KeepsSingleRow_AndOverwrites()
    {
        IServiceProvider services = GetServices();
        var store = services.GetRequiredService<IOutlierSettingsStore>();

        await store.SaveAsync(new OutlierSettings(true, 3.0, 30, 200), CancellationToken);
        await store.SaveAsync(new OutlierSettings(true, 4.0, 10, 100), CancellationToken);

        var loaded = await store.GetAsync(CancellationToken);
        loaded.Should().NotBeNull();
        loaded.Should().Match<OutlierSettings>(s => s.SigmaMultiplier == 4.0 && s.MinSampleCount == 10);

        var rowCount = await services.GetRequiredService<Func<StorageDbContext>>()()
            .Set<OutlierSettingsEntity>()
            .CountAsync(CancellationToken);
        rowCount.Should().Be(1);
    }
}
