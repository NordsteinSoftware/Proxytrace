using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Time;
using Nordstein.Core.Testing;
using NSubstitute;
using Proxytrace.Application.Ingestion.Internal;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Application.Tests.Ingestion;

[TestClass]
public sealed class ScopeAdmissionTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AdmitAsync_AfterTheFullProjectMemoExpires_TriesToAdmitAgain()
    {
        // The memo must not lock a project out of new scopes once retention or a delete frees a slot.
        var scopes = Substitute.For<IScopeRepository>();
        var projectId = Guid.NewGuid();
        var newScopeId = Guid.NewGuid();
        scopes.AdmitAsync(projectId, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null), Task.FromResult<Guid?>(newScopeId));
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(_ => now);
        var services = GetServices(builder =>
        {
            builder.RegisterInstance(scopes).As<IScopeRepository>();
            builder.RegisterInstance(clock).As<IClock>();
        });
        var admission = services.GetRequiredService<ScopeAdmission>();

        var whileFull = await admission.AdmitAsync(projectId, "billing", CancellationToken);
        now += ScopeAdmission.FullProjectMemo / 2;
        var withinMemo = await admission.AdmitAsync(projectId, "billing", CancellationToken);
        now += ScopeAdmission.FullProjectMemo;
        var afterMemo = await admission.AdmitAsync(projectId, "billing", CancellationToken);

        whileFull.Should().BeNull();
        withinMemo.Should().BeNull();
        afterMemo.Should().Be(newScopeId);
        await scopes.Received(2).AdmitAsync(projectId, "billing", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
