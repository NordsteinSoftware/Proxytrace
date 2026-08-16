using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Session.Internal;

internal record Session : DomainEntity<ISession>, ISession
{
    /// <summary>
    /// Gets the external key.
    /// </summary>
    public string ExternalKey { get; }
    /// <summary>
    /// Gets the project id.
    /// </summary>
    public Guid ProjectId { get; }
    /// <summary>
    /// Gets the last activity at.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; }
    /// <summary>
    /// Gets the trace count.
    /// </summary>
    public int TraceCount { get; }
    /// <summary>
    /// Gets the total tokens.
    /// </summary>
    public long TotalTokens { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Session"/> class.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Session"/> class.
    /// </summary>
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

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(ExternalKey);
        yield return Validation.NotDefault(ProjectId);
        yield return Validation.NotDefault(LastActivityAt);
    }
}
