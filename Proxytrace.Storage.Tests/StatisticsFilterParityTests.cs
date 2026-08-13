using System.Reflection;
using AwesomeAssertions;
using Proxytrace.Domain.Statistics;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Exhaustiveness guard for the two <see cref="StatisticsFilter"/> translation paths in
/// <c>AgentCallStatsQueries</c>: the LINQ chokepoint <c>Query()</c> and the raw-SQL
/// <c>BuildLatencyWhere()</c> (the percentile paths). Both must handle every filter member, but
/// nothing in the type system ties them together — a new member silently ignored by one path would
/// make the latency percentiles disagree with every other aggregate for the same filter.
/// <para>
/// A behavioral parity test is not possible on the in-memory provider (both latency paths fall back
/// to the same LINQ there), so this locks the member list instead: adding a member to
/// <see cref="StatisticsFilter"/> fails this test until <c>Query()</c>, <c>BuildLatencyWhere()</c>
/// and the list below are all updated together.
/// </para>
/// </summary>
[TestClass]
public sealed class StatisticsFilterParityTests : BaseTest<Module>
{
    /// <summary>
    /// Every <see cref="StatisticsFilter"/> member translated by BOTH <c>Query()</c> and
    /// <c>BuildLatencyWhere()</c>. Only extend this list in the same change that teaches both
    /// paths (and their tests) about the new member.
    /// </summary>
    private static readonly string[] MembersHandledByBothFilterPaths =
    [
        nameof(StatisticsFilter.From),
        nameof(StatisticsFilter.To),
        nameof(StatisticsFilter.ProjectId),
        nameof(StatisticsFilter.AgentId),
        nameof(StatisticsFilter.EndpointId),
        nameof(StatisticsFilter.ExcludeSystemAgents),
        // #483: the multi-project scope. Query() applies it as a semi-join against
        // AgentVersion(Project); BuildLatencyWhere() as "= ANY(@projectIds)" over one uuid[]
        // parameter — see StatisticsFilterWhereTests.
        nameof(StatisticsFilter.ProjectIds),
    ];

    [TestMethod]
    public void StatisticsFilter_EveryMember_IsHandledByBothQueryAndBuildLatencyWhere()
    {
        string[] actual = typeof(StatisticsFilter)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(StatisticsFilter))
            .Select(p => p.Name)
            .ToArray();

        actual.Should().BeEquivalentTo(
            MembersHandledByBothFilterPaths,
            "AgentCallStatsQueries.Query() and AgentCallStatsQueries.BuildLatencyWhere() must both " +
            "translate every StatisticsFilter member — update both paths and this list together");
    }
}
