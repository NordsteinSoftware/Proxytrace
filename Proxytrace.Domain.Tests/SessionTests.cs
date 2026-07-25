using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;

namespace Proxytrace.Domain.Tests;

[TestClass]
public class SessionTests : DomainTest<Module>
{
    [TestMethod]
    public async Task Session_CreateNew_PersistsAndValidates()
    {
        var services = GetServices();
        var project = await GetOrCreate<IProject>(services);
        var factory = services.GetRequiredService<ISession.CreateNew>();

        var session = factory(
            externalKey: "checkout-run-42",
            projectId: project.Id,
            lastActivityAt: DateTimeOffset.UtcNow,
            traceCount: 1,
            totalTokens: 120);
        await session.AddAsync(CancellationToken);

        var loaded = await services.GetRequiredService<IRepository<ISession>>().GetAsync(session.Id, CancellationToken);
        loaded.ExternalKey.Should().Be("checkout-run-42");
        loaded.TraceCount.Should().Be(1);
    }

    [TestMethod]
    public void SessionIdDerivation_SameInputs_IsDeterministic()
    {
        var projectId = Guid.NewGuid();
        SessionIdDerivation.Derive(projectId, "run-1")
            .Should().Be(SessionIdDerivation.Derive(projectId, "run-1"));
    }

    [TestMethod]
    public void SessionIdDerivation_DifferentProjects_DifferentIds()
    {
        SessionIdDerivation.Derive(Guid.NewGuid(), "run-1")
            .Should().NotBe(SessionIdDerivation.Derive(Guid.NewGuid(), "run-1"));
    }

    [TestMethod]
    public void SessionIdDerivation_TruncateKey_CapsAt200()
    {
        var key = new string('x', 500);
        SessionIdDerivation.TruncateKey(key).Length.Should().Be(ISession.MaxExternalKeyLength);
        SessionIdDerivation.TruncateKey("short").Should().Be("short");
    }
}
