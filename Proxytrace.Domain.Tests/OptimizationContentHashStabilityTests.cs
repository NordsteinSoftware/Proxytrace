using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.Tools;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

/// <summary>
/// Golden tests pinning the optimization content-hash to literal values. The
/// theory-equals-proposal test only proves the two agree; because both now hash through the
/// same helper they would drift together and that test would not notice. These literals catch
/// any accidental change to the envelope or serializer that would break deduplication against
/// content hashes already stored in the database.
/// </summary>
[TestClass]
public sealed class OptimizationContentHashStabilityTests : BaseTest<Module>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EndpointId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void ForSystemPrompt_IsStable()
    {
        var serializer = GetServices().GetRequiredService<ISerializer>();

        var hash = OptimizationContentHash.ForSystemPrompt(serializer, AgentId, "You are a helpful assistant.");

        hash.Should().Be("10a7667241198bdf1f972bdffc9447c0cb3a0c1f6a665bdfdc0449a20f696738");
    }

    [TestMethod]
    public void ForModelSwitch_IsStable()
    {
        var serializer = GetServices().GetRequiredService<ISerializer>();

        var hash = OptimizationContentHash.ForModelSwitch(serializer, AgentId, EndpointId);

        hash.Should().Be("14c01be162ab89dace5ea0fe3750973865829cdccbe7283439ec5750775a81b1");
    }

    [TestMethod]
    public void ForTools_IsStable()
    {
        var serializer = GetServices().GetRequiredService<ISerializer>();
        var tools = new List<ToolSpecification>
        {
            new("search", "Search the web.", ToolArguments.None),
        };

        var hash = OptimizationContentHash.ForTools(serializer, AgentId, tools);

        hash.Should().Be("3e6af07edab2b6d54676a286cb687bc6b717766294fc1f37c034cff825834701");
    }
}
