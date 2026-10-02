using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Testing;
using Proxytrace.Storage.Internal.Entities.AgentCall;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// The captured request/response/model-parameter payloads are stored as plain JSON text and only
/// deserialized by the mapper for the rows a query actually returns. They must never be mapped with
/// an EF value converter: the in-memory provider (the kiosk demo and the unit tests) has no key
/// index, so every lookup — even a primary-key Find — enumerates the whole table, and it runs every
/// column's converter for every row it enumerates. With converters on these JSON columns a single
/// key lookup cost one deserialization per stored trace, which turned the kiosk's trace detail into a
/// full-table JSON parse and a loop of updates into an apparent hang.
/// </summary>
[TestClass]
public sealed class AgentCallPayloadMappingTests : BaseTest<Module>
{
    [TestMethod]
    [DataRow(nameof(AgentCallEntity.Request))]
    [DataRow(nameof(AgentCallEntity.Response))]
    [DataRow(nameof(AgentCallEntity.ModelParameters))]
    public void PayloadColumn_IsPlainTextWithoutValueConverter(string propertyName)
    {
        IServiceProvider services = GetServices();
        var context = services.GetRequiredService<Func<StorageDbContext>>()();

        var property = context.Model.FindEntityType(typeof(AgentCallEntity))?.FindProperty(propertyName);

        property.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(property);
        property.ClrType.Should().Be<string>();
        property.GetValueConverter().Should().BeNull();
    }
}
