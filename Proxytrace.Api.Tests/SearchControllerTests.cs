using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Configuration;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Search;
using Proxytrace.Domain.ProjectSearchSettings;
using Proxytrace.Domain.Search;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class SearchControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Search_QueryTooShort_ReturnsBadRequest()
    {
        IServiceProvider services = GetServicesWithStubs();
        var controller = ResolveController(services);

        var result = await controller.Search(Guid.NewGuid(), "a", CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Search_QueryTooLong_ReturnsBadRequest()
    {
        IServiceProvider services = GetServicesWithStubs();
        var controller = ResolveController(services);

        var result = await controller.Search(Guid.NewGuid(), new string('x', 201), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Search_WhitespaceQuery_ReturnsBadRequest()
    {
        IServiceProvider services = GetServicesWithStubs();
        var controller = ResolveController(services);

        var result = await controller.Search(Guid.NewGuid(), "   ", CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Search_ValidQuery_DelegatesToService()
    {
        var svc = Substitute.For<ISearchService>();
        var projectId = Guid.NewGuid();
        svc.SearchAsync(projectId, "hello", Arg.Any<CancellationToken>())
            .Returns(new SearchResults([new SearchHit(SearchKind.Agent, Guid.NewGuid(), "title", "snip", 1.0d, new Dictionary<string, string>())]));
        IServiceProvider services = GetServicesWithStubs(b => b.RegisterInstance(svc).As<ISearchService>());
        var controller = ResolveController(services);

        var result = await controller.Search(projectId, "hello", CancellationToken);

        var ok = (OkObjectResult)(result.Result ?? throw new InvalidOperationException());
        var val = ((SearchResultsDto?)ok.Value);
        val.Should().NotBeNull();
        val.Hits.Should().ContainSingle();
    }

    [TestMethod]
    public async Task UpdateSettings_NoKinds_ReturnsBadRequest()
    {
        IServiceProvider services = GetServicesWithStubs();
        var controller = ResolveController(services);

        var result = await controller.UpdateSettings(
            Guid.NewGuid(),
            new SearchIndexingSettingsDto(true, [], false, 100),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task UpdateSettings_UnknownKind_ReturnsBadRequest()
    {
        IServiceProvider services = GetServicesWithStubs();
        var controller = ResolveController(services);

        var result = await controller.UpdateSettings(
            Guid.NewGuid(),
            new SearchIndexingSettingsDto(true, ["nonsense"], false, 100),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Reindex_DelegatesToIndexer()
    {
        var indexer = Substitute.For<ISearchIndexer>();
        IServiceProvider services = GetServicesWithStubs(b => b.RegisterInstance(indexer).As<ISearchIndexer>());
        var controller = ResolveController(services);
        var projectId = Guid.NewGuid();

        var result = await controller.Reindex(projectId, CancellationToken);

        await indexer.Received(1).ReindexProjectAsync(projectId, Arg.Any<CancellationToken>());
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task GetStatus_ReportsIndexerState()
    {
        var indexer = Substitute.For<ISearchIndexer>();
        var stats = Substitute.For<ISearchIndexStatistics>();
        var tracker = Substitute.For<IReindexStateTracker>();
        var projectId = Guid.NewGuid();
        stats.CountAsync(projectId, Arg.Any<CancellationToken>()).Returns(7);
        stats.LastIndexedAtAsync(projectId, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);
        tracker.IsReindexing(projectId).Returns(true);

        IServiceProvider services = GetServicesWithStubs(b =>
        {
            b.RegisterInstance(stats).As<ISearchIndexStatistics>();
            b.RegisterInstance(tracker).As<IReindexStateTracker>();
            b.RegisterInstance(indexer).As<ISearchIndexer>();
        });
        var controller = ResolveController(services);

        var result = await controller.GetStatus(projectId, CancellationToken);

        var ok = (OkObjectResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (SearchIndexStatusDto?)ok.Value;
        dto.Should().NotBeNull();
        dto.DocumentCount.Should().Be(7);
        dto.IsReindexing.Should().BeTrue();
    }

    private IServiceProvider GetServicesWithStubs(Action<ContainerBuilder>? extra = null)
    {
        return GetServices(b =>
        {
            b.RegisterInstance(Substitute.For<ISearchService>()).As<ISearchService>().IfNotRegistered(typeof(ISearchService));
            b.RegisterInstance(Substitute.For<ISearchIndexer>()).As<ISearchIndexer>().IfNotRegistered(typeof(ISearchIndexer));
            extra?.Invoke(b);
        });
    }

    private static SearchController ResolveController(IServiceProvider services) => new(
        services.GetRequiredService<ISearchService>(),
        services.GetRequiredService<ISearchIndexer>(),
        services.GetRequiredService<IProjectSearchSettingsResolver>(),
        services.GetRequiredService<ISearchIndexStatistics>(),
        services.GetRequiredService<IReindexStateTracker>(),
        services.GetRequiredService<IProjectSearchSettings.CreateNew>(),
        services.GetRequiredService<SearchRequestOptions>(),
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());
}
