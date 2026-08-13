using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Statistics;
using Proxytrace.Storage.Internal.Statistics;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Covers the raw-SQL half of <see cref="StatisticsFilter"/> translation — the <c>WHERE</c> fragment
/// the latency/percentile paths build by hand. Those paths only run on a relational provider, so the
/// behavioural tests (in-memory) never reach them; this asserts the generated SQL and its parameters
/// directly instead.
/// </summary>
[TestClass]
public sealed class StatisticsFilterWhereTests : BaseTest<Module>
{
    [TestMethod]
    public void BuildLatencyWhere_WithOneProject_KeepsTheEqualityPredicate()
    {
        IServiceProvider services = GetServices();
        StorageDbContext context = services.GetRequiredService<Func<StorageDbContext>>()();
        Guid projectId = Guid.NewGuid();

        var (where, parameters) = AgentCallStatsQueries.BuildLatencyWhere(
            context, new StatisticsFilter(ProjectId: projectId));

        // The common case must keep its equality predicate rather than degrade into a
        // single-element array comparison, which the planner can cost differently.
        where.Should().Contain("\"Project\" = @projectId").And.NotContain("ANY");
        var parameter = parameters.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("@projectId");
        parameter.Value.Should().Be(projectId);
    }

    [TestMethod]
    public void BuildLatencyWhere_WithSeveralProjects_ComparesAgainstOneArrayParameter()
    {
        IServiceProvider services = GetServices();
        StorageDbContext context = services.GetRequiredService<Func<StorageDbContext>>()();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        var (where, parameters) = AgentCallStatsQueries.BuildLatencyWhere(
            context, new StatisticsFilter(ProjectIds: [first, second]));

        // = ANY over a single uuid[] parameter: the ids never reach the statement text (so the SQL
        // stays parameterised and its text constant regardless of how many projects there are).
        where.Should().Contain("\"Project\" = ANY(@projectIds)");
        where.Should().NotContain(first.ToString()).And.NotContain(second.ToString());
        var parameter = parameters.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("@projectIds");
        parameter.Value.Should().BeOfType<Guid[]>().Which.Should().Equal(first, second);
    }

    [TestMethod]
    public void BuildLatencyWhere_WithAnEmptyProjectSet_AddsNoClause()
    {
        IServiceProvider services = GetServices();
        StorageDbContext context = services.GetRequiredService<Func<StorageDbContext>>()();

        var (where, parameters) = AgentCallStatsQueries.BuildLatencyWhere(
            context, new StatisticsFilter(ProjectIds: []));

        // Mirrors Query(): an empty set is "not restricted by a set", never "match nothing" — an
        // endpoint short-circuits an empty scope before it builds a filter at all.
        where.Should().BeEmpty();
        parameters.Should().BeEmpty();
    }
}
