using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Search.Internal;
using Proxytrace.Application.Search.Internal.Mappers;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Tests.Search;

/// <summary>
/// Guards the fix for the indexing bypass: writes reach the index via the entity-change event
/// stream (every repository interface), not a repository decorator (only IRepository&lt;T&gt;).
/// </summary>
[TestClass]
public sealed class EntityChangeIndexingServiceTests
{
    public required TestContext TestContext { get; init; }

    [TestMethod]
    public async Task Added_SearchableKind_EnqueuesIndex()
    {
        var entityId = Guid.NewGuid();
        var indexed = new TaskCompletionSource<(SearchKind Kind, Guid Id)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var indexer = Substitute.For<ISearchIndexer>();
        indexer.IndexAsync(default, default, default, default).ReturnsForAnyArgs(ci =>
        {
            indexed.TrySetResult((ci.ArgAt<SearchKind>(0), ci.ArgAt<Guid>(2)));
            return Task.CompletedTask;
        });

        var (events, channel) = ChannelBackedEvents();
        var service = new EntityChangeIndexingService(
            events, indexer, [AgentCallMapper()], NullLogger<EntityChangeIndexingService>.Instance);

        await service.StartAsync(TestContext.CancellationToken);
        try
        {
            await channel.Writer.WriteAsync(
                new EntityChangedEvent(entityId, typeof(IAgentCall), EntityChangeType.Added),
                TestContext.CancellationToken);

            var result = await indexed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            result.Should().Be((SearchKind.AgentCall, entityId));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task Removed_SearchableKind_EnqueuesRemove()
    {
        var entityId = Guid.NewGuid();
        var removed = new TaskCompletionSource<(SearchKind Kind, Guid Id)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var indexer = Substitute.For<ISearchIndexer>();
        indexer.RemoveAsync(default, default, default).ReturnsForAnyArgs(ci =>
        {
            removed.TrySetResult((ci.ArgAt<SearchKind>(0), ci.ArgAt<Guid>(1)));
            return Task.CompletedTask;
        });

        var (events, channel) = ChannelBackedEvents();
        var service = new EntityChangeIndexingService(
            events, indexer, [AgentCallMapper()], NullLogger<EntityChangeIndexingService>.Instance);

        await service.StartAsync(TestContext.CancellationToken);
        try
        {
            await channel.Writer.WriteAsync(
                new EntityChangedEvent(entityId, typeof(IAgentCall), EntityChangeType.Removed),
                TestContext.CancellationToken);

            var result = await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            result.Should().Be((SearchKind.AgentCall, entityId));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task NonSearchableType_IsIgnored()
    {
        var searchableId = Guid.NewGuid();
        var indexed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

        var indexer = Substitute.For<ISearchIndexer>();
        indexer.IndexAsync(default, default, default, default).ReturnsForAnyArgs(ci =>
        {
            indexed.TrySetResult(ci.ArgAt<Guid>(2));
            return Task.CompletedTask;
        });

        var (events, channel) = ChannelBackedEvents();
        var service = new EntityChangeIndexingService(
            events, indexer, [AgentCallMapper()], NullLogger<EntityChangeIndexingService>.Instance);

        await service.StartAsync(TestContext.CancellationToken);
        try
        {
            // An unmapped type first, then a mapped one. When the mapped event is processed the
            // unmapped one must already have been skipped — so only one index call happens.
            await channel.Writer.WriteAsync(
                new EntityChangedEvent(Guid.NewGuid(), typeof(string), EntityChangeType.Added),
                TestContext.CancellationToken);
            await channel.Writer.WriteAsync(
                new EntityChangedEvent(searchableId, typeof(IAgentCall), EntityChangeType.Added),
                TestContext.CancellationToken);

            var result = await indexed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            result.Should().Be(searchableId);
            await indexer.DidNotReceive().IndexAsync(
                Arg.Any<SearchKind>(), Arg.Any<Guid>(), Arg.Is<Guid>(g => g != searchableId), Arg.Any<CancellationToken>());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static IDocumentMapper AgentCallMapper()
    {
        var mapper = Substitute.For<IDocumentMapper>();
        mapper.Kind.Returns(SearchKind.AgentCall);
        mapper.EntityType.Returns(typeof(IAgentCall));
        return mapper;
    }

    private static (IEntityEventService Events, Channel<EntityChangedEvent> Channel) ChannelBackedEvents()
    {
        var channel = Channel.CreateUnbounded<EntityChangedEvent>();
        var events = Substitute.For<IEntityEventService>();
        events.Subscribe(Arg.Any<CancellationToken>(), Arg.Any<Type?>()).Returns(channel.Reader);
        return (events, channel);
    }
}
