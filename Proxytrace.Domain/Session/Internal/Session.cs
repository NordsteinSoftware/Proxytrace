using System.ComponentModel.DataAnnotations;
using Proxytrace.Common.Validation;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Session.Internal;

internal record Session : DomainEntity<ISession>, ISession
{
    public string ExternalKey { get; }
    public Guid ProjectId { get; }
    public DateTimeOffset LastActivityAt { get; }
    public int TraceCount { get; }
    public long TotalTokens { get; }

    public Session(
        string externalKey,
        Guid projectId,
        DateTimeOffset lastActivityAt,
        int traceCount,
        long totalTokens,
        IRepository<ISession> repository) : base(repository)
    {
        ExternalKey = externalKey;
        ProjectId = projectId;
        LastActivityAt = lastActivityAt;
        TraceCount = traceCount;
        TotalTokens = totalTokens;
    }

    public Session(
        string externalKey,
        Guid projectId,
        DateTimeOffset lastActivityAt,
        int traceCount,
        long totalTokens,
        IDomainEntityData existing,
        IRepository<ISession> repository) : base(existing, repository)
    {
        ExternalKey = externalKey;
        ProjectId = projectId;
        LastActivityAt = lastActivityAt;
        TraceCount = traceCount;
        TotalTokens = totalTokens;
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(ExternalKey);
        yield return Validation.NotDefault(ProjectId);
        yield return Validation.NotDefault(LastActivityAt);
    }
}
