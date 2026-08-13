using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.AuditLog.Internal;

internal class AuditLogEntryGenerator : DomainEntityGenerator<IAuditLogEntry>, IAuditLogEntryGenerator
{
    private readonly IAuditLogEntry.CreateNew factory;
    private readonly IAuditLogEntry.CreateExisting createExisting;

    public AuditLogEntryGenerator(
        IAuditLogEntry.CreateNew factory,
        IAuditLogEntry.CreateExisting createExisting,
        IRepository<IAuditLogEntry> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.createExisting = createExisting;
    }

    public override Task<IAuditLogEntry> GenerateAsync(CancellationToken cancellationToken = default)
        => factory(
                action: random.Enum<AuditAction>(),
                actorType: AuditActorType.User,
                actorUserId: Guid.NewGuid(),
                actorEmail: $"{random.UniqueString()}@example.com",
                actorApiKeyId: null,
                projectId: Guid.NewGuid(),
                targetType: $"Target.{random.UniqueString()}",
                targetId: Guid.NewGuid(),
                targetLabel: random.UniqueString(),
                details: null,
                outcome: AuditOutcome.Success)
            .ToTaskResult();

    public async Task<IAuditLogEntry> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var entry = await CreateAsync(cancellationToken);
        var modified = createExisting(
            entry.Action,
            entry.ActorType,
            entry.ActorUserId,
            entry.ActorEmail,
            entry.ActorApiKeyId,
            entry.ProjectId,
            entry.TargetType,
            entry.TargetId,
            entry.TargetLabel,
            entry.Details,
            entry.Outcome,
            new ModifiedDomainEntityData(entry.Id, createdAt, entry.UpdatedAt));
        return await modified.UpdateAsync(cancellationToken);
    }

    private record ModifiedDomainEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
