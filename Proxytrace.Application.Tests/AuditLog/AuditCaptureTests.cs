using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.AuditLog;
using Proxytrace.Application.AuditLog.Internal;
using Proxytrace.Application.ErrorLog.Internal;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.AuditLog;

[TestClass]
public sealed class AuditCaptureTests : BaseTest<Module>
{
    private static ILogger<Audit> CreateLogger(IAuditChannel channel, IAuditActorAccessor? actorAccessor)
    {
        var factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new AuditChannelLoggerProvider(channel, actorAccessor));
        });
        return new Logger<Audit>(factory);
    }

    [TestMethod]
    public async Task LogAudit_WithUserActor_EnqueuesEntryEnrichedWithActorAndFields()
    {
        var channel = new AuditChannel();
        var actor = new AuditActor(AuditActorType.User, Guid.NewGuid(), "admin@example.com", null);
        var accessor = Substitute.For<IAuditActorAccessor>();
        accessor.GetCurrentActor().Returns(actor);
        var logger = CreateLogger(channel, accessor);

        var projectId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        logger.LogAudit(AuditAction.ApiKeyMinted, "ApiKey", targetId, "prod key", projectId, details: "{\"scopes\":\"McpRead\"}");

        var entries = await DrainAsync(channel);

        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.Action.Should().Be(AuditAction.ApiKeyMinted);
        entry.ActorType.Should().Be(AuditActorType.User);
        entry.ActorUserId.Should().Be(actor.UserId);
        entry.ActorEmail.Should().Be("admin@example.com");
        entry.ProjectId.Should().Be(projectId);
        entry.TargetType.Should().Be("ApiKey");
        entry.TargetId.Should().Be(targetId);
        entry.TargetLabel.Should().Be("prod key");
        entry.Details.Should().Contain("McpRead");
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task LogAudit_WithNoActorAccessor_RecordsSystemActor()
    {
        var channel = new AuditChannel();
        var logger = CreateLogger(channel, actorAccessor: null);

        logger.LogAudit(AuditAction.LicenseSet, "License");

        var entries = await DrainAsync(channel);

        entries.Should().ContainSingle();
        entries[0].ActorType.Should().Be(AuditActorType.System);
        entries[0].ActorUserId.Should().BeNull();
        entries[0].ActorEmail.Should().BeNull();
        entries[0].ProjectId.Should().BeNull();
    }

    [TestMethod]
    public void Provider_NonAuditCategory_ReturnsNullLogger()
    {
        var channel = new AuditChannel();
        using var provider = new AuditChannelLoggerProvider(channel, actorAccessor: null);

        var auditCategory = typeof(Audit).FullName ?? typeof(Audit).Name;

        provider.CreateLogger("Proxytrace.Some.Other.Service").Should().BeOfType<NullLogger>();
        provider.CreateLogger(auditCategory).Should().BeOfType<AuditChannelLogger>();
    }

    [TestMethod]
    public async Task Logger_OrdinaryLogOnAuditCategory_IsNotCaptured()
    {
        var channel = new AuditChannel();
        // A plain string log on the audit category is not an AuditState — it must be ignored.
        ILogger logger = new AuditChannelLogger(channel, actorAccessor: null);

        logger.LogInformation("not an audit event");

        var entries = await DrainAsync(channel);
        entries.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ErrorLogPipeline_DoesNotCaptureInformationLevelAuditCategory()
    {
        // Audit events log at Information; the error-log capture only takes >= Error, so there is no
        // double capture of audit entries into the ApplicationError table.
        var errorChannel = new ErrorLogChannel();
        ILogger errorLogger = new ErrorLogChannelLogger("Proxytrace.Domain.AuditLog.Audit", errorChannel);

        errorLogger.LogInformation("audit happened");

        var captured = new List<ErrorLogEntry>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var entry in errorChannel.ReadAllAsync(cts.Token))
            {
                captured.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        captured.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Writer_PersistsEntry_UsingOccurredAtAsCreatedAt()
    {
        var services = GetServices();
        var channel = services.GetRequiredService<IAuditChannel>();
        var writer = services.GetRequiredService<AuditWriter>();
        var repository = services.GetRequiredService<IAuditLogRepository>();

        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var projectId = Guid.NewGuid();
        channel.TryWrite(new AuditCapture(
            AuditAction.TestRunStarted, AuditActorType.System, null, null, null,
            projectId, "TestRunGroup", Guid.NewGuid(), "Nightly", null, AuditOutcome.Success, occurredAt));

        await writer.StartAsync(CancellationToken);
        try
        {
            for (var i = 0; i < 100 && await repository.CountAsync(CancellationToken) == 0; i++)
            {
                await Task.Delay(20, CancellationToken);
            }
        }
        finally
        {
            await writer.StopAsync(CancellationToken);
        }

        var all = await repository.GetAllAsync(CancellationToken);
        var stored = all.Should().ContainSingle().Subject;
        stored.Action.Should().Be(AuditAction.TestRunStarted);
        stored.ProjectId.Should().Be(projectId);
        stored.CreatedAt.Should().BeCloseTo(occurredAt, TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void Channel_TryRead_DrainsBufferedEntriesThenReturnsFalse()
    {
        // TryRead backs AuditWriter's shutdown drain: it must hand back each buffered entry and then
        // report empty, so the writer can flush the backlog on stop without losing anything.
        var channel = new AuditChannel();
        channel.TryWrite(SampleCapture());
        channel.TryWrite(SampleCapture());

        channel.TryRead(out var first).Should().BeTrue();
        first.Should().NotBeNull();
        channel.TryRead(out _).Should().BeTrue();
        channel.TryRead(out var none).Should().BeFalse();
        none.Should().BeNull();
    }

    [TestMethod]
    public async Task Channel_UnderVolume_NeverDropsEntries()
    {
        // The channel is documented as unbounded + lossless: every TryWrite succeeds and every entry
        // is later drained, with no drop under volume (unlike the bounded drop-oldest error-log channel).
        var channel = new AuditChannel();
        const int count = 500;
        for (var i = 0; i < count; i++)
        {
            channel.TryWrite(SampleCapture()).Should().BeTrue();
        }

        var drained = await DrainAsync(channel);

        drained.Should().HaveCount(count);
    }

    private static AuditCapture SampleCapture()
        => new(
            AuditAction.TestRunStarted, AuditActorType.System, null, null, null,
            Guid.NewGuid(), "TestRunGroup", Guid.NewGuid(), "label", null, AuditOutcome.Success,
            DateTimeOffset.UtcNow);

    private async Task<List<AuditCapture>> DrainAsync(IAuditChannel channel)
    {
        var entries = new List<AuditCapture>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var entry in channel.ReadAllAsync(cts.Token))
            {
                entries.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            // expected — drain whatever is buffered then cancel.
        }

        return entries;
    }
}
